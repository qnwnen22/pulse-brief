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
        var contentStatusCounts = articles
            .GroupBy(article => string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var summaryProviders = summaries
            .GroupBy(summary => string.IsNullOrWhiteSpace(summary.Provider) ? "unknown" : summary.Provider)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

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
                duplicateArticleCount = Math.Max(0, articles.Count - effectiveArticleCount),
                groupCount = groups.Count,
                summaryCount = summaries.Count,
                sourceCount = articles
                    .Select(article => article.Source)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                latestPublishedAt = MaxOrNull(articles.Select(article => article.PublishedAt)),
                oldestPublishedAt = MinOrNull(articles.Select(article => article.PublishedAt)),
                latestArticleUpdatedAt = MaxOrNull(articles.Select(article => article.UpdatedAt))
            },
            contentFetch = new
            {
                pending = contentStatusCounts.GetValueOrDefault("pending"),
                success = contentStatusCounts.GetValueOrDefault("success"),
                failed = contentStatusCounts.GetValueOrDefault("failed"),
                successRate = Ratio(contentStatusCounts.GetValueOrDefault("success"), articles.Count)
            },
            images = new
            {
                withImage = articles.Count(article => !string.IsNullOrWhiteSpace(article.ImageUrl)),
                missingImage = articles.Count(article => string.IsNullOrWhiteSpace(article.ImageUrl)),
                coverageRate = Ratio(articles.Count(article => !string.IsNullOrWhiteSpace(article.ImageUrl)), articles.Count)
            },
            summaries = new
            {
                dailyCount = summaries.Count(summary => !summary.Date.StartsWith("weekly:", StringComparison.OrdinalIgnoreCase)),
                weeklyCount = summaries.Count(summary => summary.Date.StartsWith("weekly:", StringComparison.OrdinalIgnoreCase)),
                latestGeneratedAt = MaxOrNull(summaries.Select(summary => summary.GeneratedAt)),
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
            pipeline = pipelineRunTracker.Current,
            recentEvents = operationalLog.ReadRecentEvents(20)
        };
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
