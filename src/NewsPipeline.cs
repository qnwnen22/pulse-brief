namespace PulseBrief;

public sealed class NewsPipeline(
    AppPaths paths,
    RssCollector rssCollector,
    ArticleStore store,
    EmbeddingService embeddingService,
    ArticleClusterer clusterer,
    BriefGenerator briefGenerator,
    DailySummaryService dailySummaryService)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<PipelineResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var feeds = await paths.ReadFeedUrlsAsync();
            var fetched = await rssCollector.FetchAsync(feeds, cancellationToken);
            var articles = await store.UpsertArticlesAsync(fetched);

            await embeddingService.EnsureEmbeddingsAsync(articles);
            await store.SaveArticlesAsync(articles);

            var groups = clusterer.GroupSimilarArticles(articles);
            var enriched = await briefGenerator.EnrichGroupsAsync(groups, articles);
            await store.SaveGroupsAsync(enriched);
            await dailySummaryService.RefreshYesterdaySummaryAsync(cancellationToken);

            return new PipelineResult(fetched.Count, articles.Count, enriched.Count, DateTimeOffset.UtcNow);
        }
        finally
        {
            _lock.Release();
        }
    }
}
