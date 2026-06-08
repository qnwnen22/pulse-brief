namespace PulseBrief;

/// <summary>저장된 이슈 그룹을 기반으로 일간/주간 요약을 만들고 OpenAI 요약 결과를 캐싱합니다.</summary>
public sealed class DailySummaryService(IArticleStore store, OpenAiDailySummaryClient openAiClient)
{
    private static readonly TimeZoneInfo KoreaTimeZone = ResolveKoreaTimeZone();

    /// <summary>지정한 날짜의 일간 요약을 조회하거나 새로 생성합니다. 기본값은 한국 시간 기준 전날입니다.</summary>
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
        var aiSummary = await openAiClient.TryGenerateAsync(summary, cancellationToken: cancellationToken);
        if (aiSummary is not null) summary = aiSummary;

        await store.SaveDailySummaryAsync(summary);
        return summary;
    }

    /// <summary>한국 시간 기준 전날 요약을 강제로 다시 생성합니다.</summary>
    public async Task RefreshYesterdaySummaryAsync(CancellationToken cancellationToken = default)
    {
        await GetOrCreateSummaryAsync(GetYesterdayInKorea(), force: true, cancellationToken);
    }

    /// <summary>종료일을 기준으로 최근 7일 주간 요약을 조회하거나 새로 생성합니다.</summary>
    public async Task<DailyIssueSummary> GetOrCreateWeeklySummaryAsync(DateOnly? endDate = null, bool force = false, CancellationToken cancellationToken = default)
    {
        var targetEndDate = endDate ?? GetTodayInKorea();
        var startDate = targetEndDate.AddDays(-6);
        var key = $"weekly:{startDate:yyyy-MM-dd}:{targetEndDate:yyyy-MM-dd}";

        if (!force)
        {
            var existing = await store.ReadDailySummaryAsync(key);
            if (existing is not null && (!openAiClient.IsConfigured || existing.Provider == "openai")) return existing;
        }

        var articles = await store.ReadArticlesAsync();
        var groups = await store.ReadGroupsAsync();
        var summary = BuildSummary(key, groups, articles, startDate, targetEndDate, "주간");
        var aiSummary = await openAiClient.TryGenerateAsync(summary, "최근 7일 주간 이슈", cancellationToken);
        if (aiSummary is not null) summary = aiSummary;

        await store.SaveDailySummaryAsync(summary);
        return summary;
    }

    /// <summary>단일 날짜에 해당하는 이슈 그룹과 기사 목록으로 로컬 일간 요약 초안을 만듭니다.</summary>
    private static DailyIssueSummary BuildSummary(string date, IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var targetDate = DateOnly.Parse(date);
        return BuildSummary(date, groups, articles, targetDate, targetDate, "전날");
    }

    /// <summary>지정한 기간에 포함된 이슈 그룹을 모아 AI 호출 전 로컬 요약 초안을 만듭니다.</summary>
    private static DailyIssueSummary BuildSummary(string date, IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles, DateOnly startDate, DateOnly endDate, string periodLabel)
    {
        var articleById = articles
            .Where(article => !string.IsNullOrWhiteSpace(article.Id))
            .GroupBy(article => article.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var targetGroups = groups
            .Where(group =>
            {
                var groupDate = ToKoreaDate(group.LatestPublishedAt);
                return groupDate >= startDate && groupDate <= endDate;
            })
            .OrderByDescending(group => group.Score)
            .ThenByDescending(group => group.ArticleCount)
            .ThenByDescending(group => group.LatestPublishedAt)
            .ToList();

        var effectiveArticleIds = targetGroups
            .SelectMany(group => EffectiveArticleIds(group, articleById))
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
                    .OrderBy(item => IsLowBriefingValueGroup(item) ? 1 : 0)
                    .ThenByDescending(item => EffectiveSourceCount(item, articleById))
                    .ThenByDescending(item => EffectiveArticleCount(item, articleById))
                    .ThenByDescending(item => item.Score)
                    .First();
                return new DailyCategorySummary
                {
                    Category = group.Key,
                    IssueCount = group.Count(),
                    ArticleCount = group.SelectMany(item => EffectiveArticleIds(item, articleById)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Summary = $"{Clean(topGroup.RepresentativeTitle)} 이슈가 가장 두드러졌고, 이 카테고리에서 {group.Count()}개 이슈가 확인됐습니다."
                };
            })
            .ToArray();
        var topIssues = targetGroups
            .GroupBy(group => group.Category)
            .SelectMany(group => group
                .OrderBy(item => IsLowBriefingValueGroup(item) ? 1 : 0)
                .ThenByDescending(item => EffectiveSourceCount(item, articleById))
                .ThenByDescending(item => EffectiveArticleCount(item, articleById))
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.LatestPublishedAt)
                .Take(4))
            .OrderByDescending(group => categorySummaries.FirstOrDefault(category => category.Category == group.Category)?.IssueCount ?? 0)
            .ThenBy(group => IsLowBriefingValueGroup(group) ? 1 : 0)
            .ThenByDescending(group => EffectiveSourceCount(group, articleById))
            .ThenByDescending(group => EffectiveArticleCount(group, articleById))
            .ThenByDescending(group => group.Score)
            .Take(20)
            .Select(group =>
            {
                var effectiveIds = EffectiveArticleIds(group, articleById).ToArray();
                return new DailyTopIssue
                {
                    Title = Clean(group.RepresentativeTitle),
                    Category = group.Category,
                    Summary = Clean(group.Summary),
                    ArticleCount = effectiveIds.Length,
                    ArticleIds = effectiveIds,
                    Score = group.Score,
                    Sources = group.Sources.Take(4).ToArray()
                };
            })
            .ToArray();

        var headline = targetGroups.Count == 0
            ? $"{date}에는 저장된 이슈가 없습니다"
            : $"{date} {periodLabel} 주요 이슈 {targetGroups.Count}건";
        var summary = targetGroups.Count == 0
            ? "해당 기간에 수집된 이슈 그룹이 아직 없습니다."
            : BuildNarrative(categorySummaries, topIssues, periodLabel);

        return new DailyIssueSummary
        {
            Date = date,
            GeneratedAt = DateTimeOffset.UtcNow,
            Provider = "local",
            Headline = headline,
            Summary = summary,
            IssueCount = targetGroups.Count,
            ArticleCount = effectiveArticleIds.Length,
            SourceCount = sourceCount,
            Categories = categorySummaries,
            TopIssues = topIssues
        };
    }

    /// <summary>포토/화보처럼 사실 흐름 요약 가치가 낮은 이슈를 대표 요약 후보에서 후순위로 밀기 위해 판별합니다.</summary>
    private static bool IsLowBriefingValueGroup(ArticleGroup group)
    {
        var text = $"{string.Join(' ', group.Sources)} {group.RepresentativeTitle} {group.SeedTitle}".ToLowerInvariant();
        return text.Contains("et포토", StringComparison.OrdinalIgnoreCase)
            || text.Contains("포토", StringComparison.OrdinalIgnoreCase)
            || text.Contains("화보", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>같은 출처, 제목, 작성자가 반복된 기사 묶음을 하나로 보아 요약용 유효 기사 ID를 반환합니다.</summary>
    private static IEnumerable<string> EffectiveArticleIds(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        return group.ArticleIds
            .Select(id => articleById.TryGetValue(id, out var article) ? article : null)
            .Where(article => article is not null)
            .GroupBy(article => DuplicateKey(article!), StringComparer.OrdinalIgnoreCase)
            .Select(duplicateGroup => duplicateGroup
                .OrderByDescending(article => article!.PublishedAt)
                .First()!.Id);
    }

    /// <summary>요약 후보 정렬에 사용할 중복 제거 기사 수입니다.</summary>
    private static int EffectiveArticleCount(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        return EffectiveArticleIds(group, articleById).Count();
    }

    /// <summary>요약 후보 정렬에 사용할 중복 제거 출처 수입니다.</summary>
    private static int EffectiveSourceCount(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        return group.ArticleIds
            .Select(id => articleById.TryGetValue(id, out var article) ? article : null)
            .Where(article => article is not null)
            .Select(article => Clean(article!.Source))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>동일 기사 반복 여부를 판단하기 위한 출처/제목/작성자 기반 키를 만듭니다.</summary>
    private static string DuplicateKey(Article article)
    {
        var author = string.IsNullOrWhiteSpace(article.Author) ? "unknown" : Clean(article.Author).ToLowerInvariant();
        return $"{Clean(article.Source).ToLowerInvariant()}|{NormalizeForDuplicate(article.Title)}|{author}";
    }

    /// <summary>중복 판정에서 문장부호와 공백 차이를 줄이기 위해 제목을 정규화합니다.</summary>
    private static string NormalizeForDuplicate(string value)
    {
        var cleaned = Clean(value).ToLowerInvariant();
        return new string(cleaned.Where(char.IsLetterOrDigit).ToArray());
    }

    /// <summary>카테고리 분포와 대표 이슈를 바탕으로 로컬 fallback용 요약 문장을 만듭니다.</summary>
    private static string BuildNarrative(IReadOnlyList<DailyCategorySummary> categories, IReadOnlyList<DailyTopIssue> topIssues, string periodLabel)
    {
        var categoryText = categories.Count == 0
            ? "특정 카테고리 쏠림은 크지 않았습니다"
            : $"{string.Join(", ", categories.Take(3).Select(category => category.Category))} 분야의 비중이 컸습니다";
        var topText = topIssues.Count == 0
            ? "대표 이슈는 아직 정리되지 않았습니다"
            : $"가장 주목할 이슈는 {topIssues[0].Title}입니다";
        return $"{categoryText}. {topText}. 아래 주요 이슈를 훑으면 {periodLabel} 뉴스 흐름을 빠르게 파악할 수 있습니다.";
    }

    /// <summary>현재 시각을 한국 시간으로 변환해 오늘 날짜를 반환합니다.</summary>
    private static DateOnly GetTodayInKorea()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, KoreaTimeZone);
        return DateOnly.FromDateTime(now.Date);
    }

    /// <summary>현재 시각을 한국 시간으로 변환해 전날 날짜를 반환합니다.</summary>
    private static DateOnly GetYesterdayInKorea()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, KoreaTimeZone);
        return DateOnly.FromDateTime(now.Date).AddDays(-1);
    }

    /// <summary>UTC 또는 임의 오프셋 시각을 한국 날짜로 변환합니다.</summary>
    private static DateOnly ToKoreaDate(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, KoreaTimeZone);
        return DateOnly.FromDateTime(local.Date);
    }

    /// <summary>요약에 들어갈 텍스트에서 HTML 흔적과 불필요한 공백을 제거합니다.</summary>
    private static string Clean(string value)
    {
        return TextCleaner.Clean(value).Trim();
    }

    /// <summary>Windows와 Linux 환경 모두에서 한국 표준 시간대를 찾습니다.</summary>
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
