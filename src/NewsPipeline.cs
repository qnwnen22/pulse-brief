namespace PulseBrief;

/// <summary>RSS 수집, 기사 본문 보강, 임베딩, 그룹화, 요약 갱신을 순서대로 실행하는 뉴스 처리 파이프라인입니다.</summary>
public sealed class NewsPipeline(
    AppPaths paths,
    RssCollector rssCollector,
    IArticleStore store,
    ArticleContentFetcher articleContentFetcher,
    EmbeddingService embeddingService,
    ArticleClusterer clusterer,
    BriefGenerator briefGenerator,
    DailySummaryService dailySummaryService,
    PipelineRunTracker pipelineRunTracker,
    OperationalLogService operationalLog)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>뉴스 수집 파이프라인을 한 번 실행하고 수집/저장/그룹화 결과를 반환합니다.</summary>
    public async Task<PipelineResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        var runId = pipelineRunTracker.MarkStarted();
        await operationalLog.RecordAsync("info", "pipeline_started", "News pipeline started.", cancellationToken: cancellationToken);
        try
        {
            var feeds = await paths.ReadFeedUrlsAsync();
            var fetched = await rssCollector.FetchAsync(feeds, cancellationToken);
            var articles = await store.UpsertArticlesAsync(fetched);

            await articleContentFetcher.EnrichMissingContentAsync(articles, Math.Max(fetched.Count, 50), cancellationToken);
            await embeddingService.EnsureEmbeddingsAsync(articles);
            await store.SaveArticlesAsync(articles);

            var groups = clusterer.GroupSimilarArticles(articles);
            var enriched = await briefGenerator.EnrichGroupsAsync(groups, articles);
            await store.SaveGroupsAsync(enriched);
            if (dailySummaryService.IsGenerationEnabled)
            {
                await dailySummaryService.EnsureScheduledSummariesAsync(cancellationToken);
            }
            else
            {
                await operationalLog.RecordAsync("info", "summary_generation_skipped", "Summary generation is disabled by configuration.", cancellationToken: cancellationToken);
            }

            var result = new PipelineResult(fetched.Count, articles.Count, enriched.Count, DateTimeOffset.UtcNow);
            pipelineRunTracker.MarkCompleted(runId, result);
            await operationalLog.RecordAsync("info", "pipeline_completed", "News pipeline completed.", new
            {
                result.FetchedCount,
                result.ArticleCount,
                result.GroupCount,
                result.UpdatedAt
            }, CancellationToken.None);
            return result;
        }
        catch (OperationCanceledException error)
        {
            pipelineRunTracker.MarkFailed(runId, error, "cancelled");
            await operationalLog.RecordAsync("warning", "pipeline_cancelled", "News pipeline was cancelled.", new
            {
                errorType = error.GetType().Name,
                error.Message
            }, CancellationToken.None);
            throw;
        }
        catch (Exception error)
        {
            pipelineRunTracker.MarkFailed(runId, error);
            await operationalLog.RecordAsync("error", "pipeline_failed", "News pipeline failed.", new
            {
                errorType = error.GetType().Name,
                error.Message
            }, CancellationToken.None);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
}
