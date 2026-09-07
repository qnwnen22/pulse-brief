using System.Text.RegularExpressions;

namespace PulseBrief;

public sealed partial class DailySummaryService
{
    private async Task<IReadOnlyList<DailyIssueSummary>> ReadDailySummariesAsync(DateOnly startDate, DateOnly endDate)
    {
        var tasks = EachDate(startDate, endDate)
            .Select(date => store.ReadDailySummaryAsync(date.ToString("yyyy-MM-dd")))
            .ToArray();
        var summaries = await Task.WhenAll(tasks);
        return summaries
            .Where(summary => summary is not null && !summary.Date.StartsWith("weekly:", StringComparison.OrdinalIgnoreCase))
            .Cast<DailyIssueSummary>()
            .OrderBy(summary => summary.Date)
            .ToArray();
    }

    private static DailyIssueSummary BuildWeeklySummaryFromDailySummaries(
        string key,
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyList<DailyIssueSummary> dailySummaries)
    {
        var summaries = dailySummaries
            .Where(summary => DateOnly.TryParse(summary.Date, out var date) && date >= startDate && date <= endDate)
            .OrderBy(summary => summary.Date)
            .ToArray();

        if (summaries.Length == 0)
        {
            return new DailyIssueSummary
            {
                Date = key,
                GeneratedAt = DateTimeOffset.UtcNow,
                Provider = "local",
                Headline = $"{startDate:yyyy-MM-dd}~{endDate:yyyy-MM-dd} 주간 요약 준비 중",
                Summary = "해당 주간에 생성된 일간 요약이 아직 없습니다.",
                IssueCount = 0,
                ArticleCount = 0,
                SourceCount = 0,
                Categories = [],
                TopIssues = []
            };
        }

        var dailyIssues = summaries
            .SelectMany(summary => summary.TopIssues.Select(issue => new DailyIssueContext(summary.Date, issue)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Issue.Title))
            .ToArray();
        var topIssues = dailyIssues
            .GroupBy(item => item.Issue.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(item => item.Issue.Score)
                    .ThenByDescending(item => item.Issue.ArticleCount)
                    .First()
                    .Issue;
                var articleIds = group
                    .SelectMany(item => item.Issue.ArticleIds)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var sources = group
                    .SelectMany(item => item.Issue.Sources)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(4)
                    .ToArray();
                var keywords = group
                    .SelectMany(item => item.Issue.Keywords)
                    .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                    .GroupBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(keywordGroup => keywordGroup.Count())
                    .ThenBy(keywordGroup => keywordGroup.Key)
                    .Take(8)
                    .Select(keywordGroup => keywordGroup.Key)
                    .ToArray();

                return new DailyTopIssue
                {
                    Title = Clean(best.Title),
                    Category = best.Category,
                    Summary = Clean(best.Summary),
                    ArticleCount = articleIds.Length > 0 ? articleIds.Length : group.Sum(item => item.Issue.ArticleCount),
                    ArticleIds = articleIds,
                    Score = Math.Min(100, group.Max(item => item.Issue.Score) + Math.Min(20, (group.Count() - 1) * 5)),
                    Sources = sources,
                    Keywords = keywords
                };
            })
            .OrderByDescending(issue => issue.Score)
            .ThenByDescending(issue => issue.ArticleCount)
            .Take(20)
            .ToArray();

        var categories = summaries
            .SelectMany(summary => summary.Categories)
            .Where(category => !string.IsNullOrWhiteSpace(category.Category))
            .GroupBy(category => category.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var categoryTopIssue = topIssues
                    .Where(issue => string.Equals(issue.Category, group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(issue => issue.Score)
                    .ThenByDescending(issue => issue.ArticleCount)
                    .FirstOrDefault();
                var issueCount = group.Sum(category => category.IssueCount);
                var articleCount = group.Sum(category => category.ArticleCount);
                var summary = categoryTopIssue is null
                    ? $"{group.Key} 분야에서 {issueCount}개 이슈와 {articleCount}개 기사가 확인됐습니다."
                    : $"{Clean(categoryTopIssue.Title)} 이슈를 중심으로 {issueCount}개 이슈와 {articleCount}개 기사가 확인됐습니다.";

                return new DailyCategorySummary
                {
                    Category = group.Key,
                    IssueCount = issueCount,
                    ArticleCount = articleCount,
                    Summary = summary
                };
            })
            .OrderByDescending(category => category.IssueCount)
            .ThenBy(category => category.Category)
            .Take(5)
            .ToArray();

        var distinctSources = topIssues
            .SelectMany(issue => issue.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var headline = $"{startDate:yyyy-MM-dd}~{endDate:yyyy-MM-dd} 주간 주요 이슈 {topIssues.Length}건";
        var summaryText = BuildWeeklyNarrative(categories, topIssues, summaries.Length);

        return new DailyIssueSummary
        {
            Date = key,
            GeneratedAt = DateTimeOffset.UtcNow,
            Provider = "local",
            Headline = headline,
            Summary = summaryText,
            IssueCount = summaries.Sum(summary => summary.IssueCount),
            ArticleCount = summaries.Sum(summary => summary.ArticleCount),
            SourceCount = Math.Max(distinctSources, summaries.Max(summary => summary.SourceCount)),
            Categories = categories,
            TopIssues = topIssues
        };
    }

    private static string BuildWeeklyNarrative(IReadOnlyList<DailyCategorySummary> categories, IReadOnlyList<DailyTopIssue> topIssues, int dailySummaryCount)
    {
        var categoryText = categories.Count == 0
            ? "주간 카테고리 흐름은 아직 충분히 쌓이지 않았습니다"
            : $"{string.Join(", ", categories.Take(3).Select(category => category.Category))} 분야의 비중이 컸습니다";
        var topText = topIssues.Count == 0
            ? "반복적으로 확인된 대표 이슈는 아직 없습니다"
            : $"가장 반복적으로 확인된 이슈는 {topIssues[0].Title}입니다";
        return $"{dailySummaryCount}개 일간 요약을 바탕으로 정리했습니다. {categoryText}. {topText}.";
    }


}
