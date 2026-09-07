using System.Text.RegularExpressions;

namespace PulseBrief;

public sealed partial class DailySummaryService
{
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

    private static DateOnly GetLatestCompletedWeekEndInKorea()
    {
        var today = GetTodayInKorea();
        var daysBack = today.DayOfWeek == DayOfWeek.Sunday
            ? 7
            : (int)today.DayOfWeek;
        return today.AddDays(-daysBack);
    }

    private static string WeeklyKey(DateOnly startDate, DateOnly endDate)
    {
        return $"weekly:{startDate:yyyy-MM-dd}:{endDate:yyyy-MM-dd}";
    }

    private static IEnumerable<DateOnly> EachDate(DateOnly startDate, DateOnly endDate)
    {
        for (var date = startDate; date.DayNumber <= endDate.DayNumber; date = date.AddDays(1))
        {
            yield return date;
        }
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

    private static string Compact(string? value, int maxLength)
    {
        var cleaned = Clean(value ?? "");
        if (cleaned.Length <= maxLength) return cleaned;
        return $"{cleaned[..maxLength].TrimEnd()}...";
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

    private sealed class SummaryCandidate
    {
        public required int Index { get; init; }

        public required ArticleGroup Group { get; init; }

        public required Article[] Articles { get; init; }

        public required string[] EffectiveArticleIds { get; init; }

        public required string[] Sources { get; init; }

        public required int EffectiveScore { get; init; }

        public required int EffectiveArticleCount { get; init; }

        public required Dictionary<string, int> KeywordWeights { get; init; }

        public required string[] Keywords { get; init; }

        public int KeywordDistributionScore { get; set; }

        public int SelectionScore { get; set; }
    }

    private sealed class SummaryIssueCandidate
    {
        public required string Title { get; init; }

        public required string Category { get; init; }

        public required string Summary { get; init; }

        public required int EffectiveArticleCount { get; init; }

        public required string[] EffectiveArticleIds { get; init; }

        public required string[] Sources { get; init; }

        public required string[] AllSources { get; init; }

        public required int Score { get; init; }

        public required int SelectionScore { get; init; }

        public required string[] Keywords { get; init; }

        public required DailyIssueEvidenceArticle[] EvidenceArticles { get; init; }

        public required DateTimeOffset LatestPublishedAt { get; init; }

        public required bool IsLowBriefingValue { get; init; }

        public required int ComponentCount { get; init; }
    }

    private sealed record DailyIssueContext(string Date, DailyTopIssue Issue);
}
