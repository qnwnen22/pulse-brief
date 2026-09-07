using System.Text.RegularExpressions;

namespace PulseBrief;

public sealed partial class DailySummaryService
{
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
            .OrderByDescending(group => EffectiveScore(group, articleById))
            .ThenByDescending(group => group.ArticleCount)
            .ThenByDescending(group => group.LatestPublishedAt)
            .ToList();

        var effectiveArticleIds = targetGroups
            .SelectMany(group => ArticleDedupe.EffectiveArticleIds(group, articleById))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceCount = targetGroups
            .SelectMany(group => EffectiveSources(group, articleById))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var candidates = targetGroups
            .Select((group, index) => BuildCandidate(group, index, articleById))
            .ToArray();
        ApplyKeywordDistributionScores(candidates);
        var issueCandidates = DeduplicateIssueCandidates(BuildIssueCandidates(candidates));

        var categorySummaries = candidates
            .GroupBy(candidate => candidate.Group.Category)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(5)
            .Select(group =>
            {
                var topIssue = issueCandidates
                    .Where(item => string.Equals(item.Category, group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.IsLowBriefingValue ? 1 : 0)
                    .ThenByDescending(item => item.SelectionScore)
                    .ThenByDescending(item => item.Sources.Length)
                    .ThenByDescending(item => item.EffectiveArticleCount)
                    .FirstOrDefault();
                var fallback = group
                    .OrderBy(item => IsLowBriefingValueGroup(item.Group) ? 1 : 0)
                    .ThenByDescending(item => item.SelectionScore)
                    .ThenByDescending(item => item.Sources.Length)
                    .ThenByDescending(item => item.EffectiveArticleCount)
                    .First();
                var title = topIssue?.Title ?? Clean(fallback.Group.RepresentativeTitle);
                var keywords = topIssue?.Keywords ?? fallback.Keywords;
                var keywordText = keywords.Length == 0
                    ? ""
                    : $" 주요 키워드는 {string.Join(", ", keywords.Take(4))}입니다.";
                return new DailyCategorySummary
                {
                    Category = group.Key,
                    IssueCount = group.Count(),
                    ArticleCount = group.SelectMany(item => item.EffectiveArticleIds).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Summary = $"{title} 이슈가 가장 두드러졌고, 이 카테고리에서 {group.Count()}개 이슈가 확인됐습니다.{keywordText}"
                };
            })
            .ToArray();
        var topIssues = issueCandidates
            .GroupBy(candidate => candidate.Category)
            .SelectMany(group => group
                .OrderBy(item => item.IsLowBriefingValue ? 1 : 0)
                .ThenByDescending(item => item.SelectionScore)
                .ThenByDescending(item => item.Sources.Length)
                .ThenByDescending(item => item.EffectiveArticleCount)
                .ThenByDescending(item => item.LatestPublishedAt)
                .Take(4))
            .OrderByDescending(candidate => categorySummaries.FirstOrDefault(category => category.Category == candidate.Category)?.IssueCount ?? 0)
            .ThenBy(candidate => candidate.IsLowBriefingValue ? 1 : 0)
            .ThenByDescending(candidate => candidate.SelectionScore)
            .ThenByDescending(candidate => candidate.Sources.Length)
            .ThenByDescending(candidate => candidate.EffectiveArticleCount)
            .Take(20)
            .Select(ToDailyTopIssue)
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


}
