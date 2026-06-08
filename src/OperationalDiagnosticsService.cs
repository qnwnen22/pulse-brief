namespace PulseBrief;

/// <summary>관리자가 배포 서버의 수집, 저장소, 요약 상태를 빠르게 확인할 수 있는 진단 응답을 생성합니다.</summary>
public sealed class OperationalDiagnosticsService(
    IArticleStore store,
    AppPaths paths,
    PipelineRunTracker pipelineRunTracker,
    OperationalLogService operationalLog,
    OpenAiDailySummaryClient openAiClient,
    IConfiguration configuration)
{
    /// <summary>민감한 기사 본문이나 토큰은 제외하고 운영에 필요한 집계 상태만 반환합니다.</summary>
    public async Task<object> BuildAsync(DateTimeOffset appStartedAt)
    {
        var articlesTask = store.ReadArticlesAsync();
        var groupsTask = store.ReadGroupsAsync();
        var summariesTask = store.ReadDailySummariesAsync();
        var feedsTask = paths.ReadFeedUrlsAsync();

        await Task.WhenAll(articlesTask, groupsTask, summariesTask, feedsTask);

        var articles = articlesTask.Result;
        var groups = groupsTask.Result;
        var summaries = summariesTask.Result;
        var feeds = feedsTask.Result;
        var now = DateTimeOffset.UtcNow;
        var effectiveArticleCount = ArticleDedupe.EffectiveArticles(articles).Length;
        var duplicateArticleCount = Math.Max(0, articles.Count - effectiveArticleCount);
        var contentStatusCounts = articles
            .GroupBy(article => string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var summaryProviders = summaries
            .GroupBy(summary => string.IsNullOrWhiteSpace(summary.Provider) ? "unknown" : summary.Provider)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var latestPublishedAt = MaxOrNull(articles.Select(article => article.PublishedAt));
        var oldestPublishedAt = MinOrNull(articles.Select(article => article.PublishedAt));
        var latestArticleUpdatedAt = MaxOrNull(articles.Select(article => article.UpdatedAt));
        var latestSummaryGeneratedAt = MaxOrNull(summaries.Select(summary => summary.GeneratedAt));
        var contentPendingCount = contentStatusCounts.GetValueOrDefault("pending");
        var contentSuccessCount = contentStatusCounts.GetValueOrDefault("success");
        var contentFailedCount = contentStatusCounts.GetValueOrDefault("failed");
        var imageCount = articles.Count(article => !string.IsNullOrWhiteSpace(article.ImageUrl));
        var pipeline = pipelineRunTracker.Current;
        var warnings = BuildWarnings(
            now,
            feeds.Count,
            articles.Count,
            duplicateArticleCount,
            contentFailedCount,
            latestArticleUpdatedAt,
            latestSummaryGeneratedAt,
            summaries.Count,
            pipeline);

        return new
        {
            generatedAt = now,
            server = new
            {
                environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
                startedAt = appStartedAt,
                uptimeMinutes = Math.Max(0, (int)Math.Round((now - appStartedAt).TotalMinutes)),
                database = configuration["Storage:Provider"] ?? "MongoDB",
                openAiConfigured = openAiClient.IsConfigured
            },
            rss = new
            {
                feedCount = feeds.Count,
                refreshIntervalMinutes = configuration.GetValue("AutoRefreshMinutes", 10)
            },
            storage = new
            {
                articleCount = articles.Count,
                effectiveArticleCount,
                duplicateArticleCount,
                groupCount = groups.Count,
                summaryCount = summaries.Count,
                sourceCount = articles
                    .Select(article => article.Source)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                latestPublishedAt,
                oldestPublishedAt,
                latestArticleUpdatedAt
            },
            contentFetch = new
            {
                pending = contentPendingCount,
                success = contentSuccessCount,
                failed = contentFailedCount,
                successRate = Ratio(contentSuccessCount, articles.Count),
                failureRate = Ratio(contentFailedCount, articles.Count)
            },
            images = new
            {
                withImage = imageCount,
                missingImage = articles.Count - imageCount,
                coverageRate = Ratio(imageCount, articles.Count)
            },
            summaries = new
            {
                dailyCount = summaries.Count(summary => !summary.Date.StartsWith("weekly:", StringComparison.OrdinalIgnoreCase)),
                weeklyCount = summaries.Count(summary => summary.Date.StartsWith("weekly:", StringComparison.OrdinalIgnoreCase)),
                latestGeneratedAt = latestSummaryGeneratedAt,
                providers = summaryProviders
            },
            categories = groups
                .GroupBy(group => string.IsNullOrWhiteSpace(group.Category) ? "미분류" : group.Category)
                .OrderByDescending(group => group.Count())
                .Select(group => new
                {
                    category = group.Key,
                    groupCount = group.Count(),
                    articleCount = group.Sum(item => item.ArticleCount)
                })
                .ToArray(),
            pipeline,
            warnings,
            recentEvents = operationalLog.ReadRecentEvents(20)
        };
    }

    /// <summary>운영자가 조치해야 할 가능성이 있는 수집/요약 상태를 경고 목록으로 변환합니다.</summary>
    private OperationalWarning[] BuildWarnings(
        DateTimeOffset now,
        int feedCount,
        int articleCount,
        int duplicateArticleCount,
        int contentFailedCount,
        DateTimeOffset? latestArticleUpdatedAt,
        DateTimeOffset? latestSummaryGeneratedAt,
        int summaryCount,
        PipelineRunSnapshot pipeline)
    {
        var warnings = new List<OperationalWarning>();
        var staleArticleHours = configuration.GetValue("Diagnostics:StaleArticleHours", 12);
        var staleSummaryHours = configuration.GetValue("Diagnostics:StaleSummaryHours", 36);
        var pipelineRunningMinutes = configuration.GetValue("Diagnostics:LongRunningPipelineMinutes", 30);
        var contentFailureRateWarning = configuration.GetValue("Diagnostics:ContentFetchFailureRateWarning", 0.5);
        var duplicateRateWarning = configuration.GetValue("Diagnostics:DuplicateArticleRateWarning", 0.35);
        var minimumArticlesForRateWarnings = configuration.GetValue("Diagnostics:MinimumArticlesForRateWarnings", 50);

        if (feedCount == 0)
        {
            warnings.Add(new OperationalWarning("rss_feed_empty", "error", "등록된 RSS 피드가 없습니다."));
        }

        if (articleCount == 0)
        {
            warnings.Add(new OperationalWarning("article_empty", "warning", "저장된 뉴스 기사가 없습니다."));
        }

        if (latestArticleUpdatedAt is not null && now - latestArticleUpdatedAt > TimeSpan.FromHours(staleArticleHours))
        {
            warnings.Add(new OperationalWarning("article_update_stale", "warning", $"최근 기사 갱신이 {staleArticleHours}시간 이상 지연되었습니다."));
        }

        if (summaryCount == 0)
        {
            warnings.Add(new OperationalWarning("summary_empty", "warning", "저장된 전날/주간 요약이 없습니다."));
        }
        else if (latestSummaryGeneratedAt is not null && now - latestSummaryGeneratedAt > TimeSpan.FromHours(staleSummaryHours))
        {
            warnings.Add(new OperationalWarning("summary_stale", "warning", $"최근 요약 생성이 {staleSummaryHours}시간 이상 지연되었습니다."));
        }

        if (pipeline.Status == "failed")
        {
            warnings.Add(new OperationalWarning("pipeline_failed", "error", "마지막 뉴스 수집 파이프라인이 실패했습니다."));
        }

        if (pipeline.IsRunning && pipeline.StartedAt is not null && now - pipeline.StartedAt > TimeSpan.FromMinutes(pipelineRunningMinutes))
        {
            warnings.Add(new OperationalWarning("pipeline_long_running", "warning", $"뉴스 수집 파이프라인이 {pipelineRunningMinutes}분 이상 실행 중입니다."));
        }

        if (articleCount >= minimumArticlesForRateWarnings)
        {
            var contentFailureRate = Ratio(contentFailedCount, articleCount);
            if (contentFailureRate >= contentFailureRateWarning)
            {
                warnings.Add(new OperationalWarning("content_fetch_failure_rate_high", "warning", $"본문 수집 실패율이 {contentFailureRate:P0}입니다."));
            }

            var duplicateRate = Ratio(duplicateArticleCount, articleCount);
            if (duplicateRate >= duplicateRateWarning)
            {
                warnings.Add(new OperationalWarning("duplicate_article_rate_high", "info", $"중복으로 판단되는 기사 비율이 {duplicateRate:P0}입니다."));
            }
        }

        return warnings.ToArray();
    }

    /// <summary>빈 시퀀스에서는 null을 반환하는 UTC 시각 최댓값 계산입니다.</summary>
    private static DateTimeOffset? MaxOrNull(IEnumerable<DateTimeOffset> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? null : items.Max();
    }

    /// <summary>빈 시퀀스에서는 null을 반환하는 UTC 시각 최솟값 계산입니다.</summary>
    private static DateTimeOffset? MinOrNull(IEnumerable<DateTimeOffset> values)
    {
        var items = values.ToArray();
        return items.Length == 0 ? null : items.Min();
    }

    /// <summary>전체 수량이 0이면 0을 반환하고, 그 외에는 소수 둘째 자리까지의 비율을 반환합니다.</summary>
    private static double Ratio(int value, int total)
    {
        return total == 0 ? 0 : Math.Round((double)value / total, 2);
    }
}

/// <summary>관리자 진단 API에서 운영자가 확인할 수 있는 상태 경고입니다.</summary>
public sealed class OperationalWarning
{
    /// <summary>경고 유형을 구분하는 안정적인 코드입니다.</summary>
    public string Code { get; init; } = "";

    /// <summary>경고 심각도입니다. info, warning, error 값을 사용합니다.</summary>
    public string Level { get; init; } = "warning";

    /// <summary>운영자가 읽을 수 있는 경고 설명입니다.</summary>
    public string Message { get; init; } = "";

    /// <summary>운영 경고 응답 모델을 생성합니다.</summary>
    public OperationalWarning(string code, string level, string message)
    {
        Code = code;
        Level = level;
        Message = message;
    }
}
