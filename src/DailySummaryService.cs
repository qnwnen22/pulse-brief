namespace PulseBrief;

public sealed class DailySummaryService(ArticleStore store, OpenAiDailySummaryClient openAiClient)
{
    private static readonly TimeZoneInfo KoreaTimeZone = ResolveKoreaTimeZone();

    public async Task<DailyIssueSummary> GetOrCreateSummaryAsync(DateOnly? date = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var targetDate = date ?? GetYesterdayInKorea();
        var key = targetDate.ToString("yyyy-MM-dd");

        if (!force)
        {
            var existing = await store.ReadDailySummaryAsync(key);
            if (existing is not null && (!openAiClient.IsConfigured || existing.Provider == "openai")) return existing;
        }

        var articles = await store.ReadArticlesAsync();
        var groups = await store.ReadGroupsAsync();
        var summary = BuildSummary(key, groups, articles);
        var aiSummary = await openAiClient.TryGenerateAsync(summary, cancellationToken);
        if (aiSummary is not null) summary = aiSummary;

        await store.SaveDailySummaryAsync(summary);
        return summary;
    }

    public async Task RefreshYesterdaySummaryAsync(CancellationToken cancellationToken = default)
    {
        await GetOrCreateSummaryAsync(GetYesterdayInKorea(), force: true, cancellationToken);
    }

    private static DailyIssueSummary BuildSummary(string date, IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var articleById = articles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);
        var targetGroups = groups
            .Where(group => ToKoreaDate(group.LatestPublishedAt).ToString("yyyy-MM-dd") == date)
            .OrderByDescending(group => group.Score)
            .ThenByDescending(group => group.ArticleCount)
            .ThenByDescending(group => group.LatestPublishedAt)
            .ToList();

        var articleIds = targetGroups
            .SelectMany(group => group.ArticleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceCount = targetGroups
            .SelectMany(group => group.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var categorySummaries = targetGroups
            .GroupBy(group => group.Category)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(5)
            .Select(group =>
            {
                var topGroup = group
                    .OrderByDescending(item => item.Score)
                    .ThenByDescending(item => item.ArticleCount)
                    .First();
                return new DailyCategorySummary
                {
                    Category = group.Key,
                    IssueCount = group.Count(),
                    ArticleCount = group.SelectMany(item => item.ArticleIds).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Summary = $"{Clean(topGroup.RepresentativeTitle)} 중심으로 {group.Count()}개 이슈가 확인됐습니다."
                };
            })
            .ToArray();
        var topIssues = targetGroups
            .Take(8)
            .Select(group => new DailyTopIssue
            {
                Title = Clean(group.RepresentativeTitle),
                Category = group.Category,
                Summary = Clean(group.Summary),
                ArticleCount = group.ArticleIds.Count(id => articleById.ContainsKey(id)),
                Score = group.Score,
                Sources = group.Sources.Take(4).ToArray()
            })
            .ToArray();

        var headline = targetGroups.Count == 0
            ? $"{date}에는 저장된 이슈가 없습니다"
            : $"{date} 주요 이슈 {targetGroups.Count}건";
        var summary = targetGroups.Count == 0
            ? "해당 날짜에 수집된 이슈 그룹이 아직 없습니다."
            : BuildNarrative(categorySummaries, topIssues);

        return new DailyIssueSummary
        {
            Date = date,
            GeneratedAt = DateTimeOffset.UtcNow,
            Provider = "local",
            Headline = headline,
            Summary = summary,
            IssueCount = targetGroups.Count,
            ArticleCount = articleIds.Length,
            SourceCount = sourceCount,
            Categories = categorySummaries,
            TopIssues = topIssues
        };
    }

    private static string BuildNarrative(IReadOnlyList<DailyCategorySummary> categories, IReadOnlyList<DailyTopIssue> topIssues)
    {
        var categoryText = categories.Count == 0
            ? "특정 카테고리 쏠림은 크지 않았습니다"
            : $"{string.Join(", ", categories.Take(3).Select(category => category.Category))} 분야의 비중이 컸습니다";
        var topText = topIssues.Count == 0
            ? "대표 이슈는 아직 정리되지 않았습니다"
            : $"가장 주목할 이슈는 {topIssues[0].Title}입니다";
        return $"{categoryText}. {topText}. 아래 주요 이슈를 훑으면 전날 뉴스 흐름을 빠르게 파악할 수 있습니다.";
    }

    private static DateOnly GetYesterdayInKorea()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, KoreaTimeZone);
        return DateOnly.FromDateTime(now.Date).AddDays(-1);
    }

    private static DateOnly ToKoreaDate(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, KoreaTimeZone);
        return DateOnly.FromDateTime(local.Date);
    }

    private static string Clean(string value)
    {
        return TextCleaner.Clean(value).Trim();
    }

    private static TimeZoneInfo ResolveKoreaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
        }
    }
}
