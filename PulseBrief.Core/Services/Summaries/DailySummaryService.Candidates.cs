using System.Text.RegularExpressions;

namespace PulseBrief;

public sealed partial class DailySummaryService
{
    private static SummaryIssueCandidate[] BuildIssueCandidates(IReadOnlyList<SummaryCandidate> candidates)
    {
        if (candidates.Count == 0) return [];

        var components = BuildRelatedCandidateComponents(candidates);
        var mergedCandidateIndexes = new HashSet<int>();
        var issueCandidates = new List<SummaryIssueCandidate>();

        foreach (var component in components.OrderByDescending(RollupPriority))
        {
            var availableComponent = component
                .Where(candidate => !mergedCandidateIndexes.Contains(candidate.Index))
                .ToArray();
            if (!ShouldCreateRollup(availableComponent, candidates.Count)) continue;

            issueCandidates.Add(ToMergedIssueCandidate(availableComponent));
            foreach (var candidate in availableComponent)
            {
                mergedCandidateIndexes.Add(candidate.Index);
            }
        }

        issueCandidates.AddRange(candidates
            .Where(candidate => !mergedCandidateIndexes.Contains(candidate.Index))
            .Select(ToSingleIssueCandidate));

        return issueCandidates.ToArray();
    }

    private static SummaryIssueCandidate[] DeduplicateIssueCandidates(IEnumerable<SummaryIssueCandidate> candidates)
    {
        var kept = new List<SummaryIssueCandidate>();
        foreach (var candidate in candidates
            .OrderBy(item => item.IsLowBriefingValue ? 1 : 0)
            .ThenByDescending(item => item.ComponentCount > 1)
            .ThenByDescending(item => item.ComponentCount)
            .ThenByDescending(item => item.SelectionScore)
            .ThenByDescending(item => item.Sources.Length)
            .ThenByDescending(item => item.EffectiveArticleCount)
            .ThenByDescending(item => item.LatestPublishedAt))
        {
            if (kept.Any(existing => AreSimilarIssueCandidates(candidate, existing))) continue;
            kept.Add(candidate);
        }

        return kept.ToArray();
    }

    private static bool AreSimilarIssueCandidates(SummaryIssueCandidate left, SummaryIssueCandidate right)
    {
        var leftKeywords = SimilarityKeywords(left);
        var rightKeywords = SimilarityKeywords(right);
        if (leftKeywords.Contains("__vote_issue__") && rightKeywords.Contains("__vote_issue__")) return true;

        var shared = leftKeywords.Intersect(rightKeywords, StringComparer.OrdinalIgnoreCase).ToArray();
        return shared.Length >= 4 || shared.Count(keyword => keyword.Length >= 4) >= 2;
    }

    private static HashSet<string> SimilarityKeywords(SummaryIssueCandidate candidate)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = $"{candidate.Title} {candidate.Summary} {string.Join(' ', candidate.Keywords)}";
        if (IsVotingShortageIssueText(text)) result.Add("__vote_issue__");

        foreach (Match match in KeywordRegex.Matches(Clean(text)))
        {
            var keyword = match.Value.ToLowerInvariant();
            if (IsRollupKeyword(keyword)) result.Add(keyword);
        }

        return result;
    }

    private static bool IsVotingShortageIssueText(string value)
    {
        var text = Clean(value);
        var hasVotingSubject = text.Contains("투표용지", StringComparison.OrdinalIgnoreCase)
            || text.Contains("투표지", StringComparison.OrdinalIgnoreCase)
            || text.Contains("투표소", StringComparison.OrdinalIgnoreCase)
            || text.Contains("개표소", StringComparison.OrdinalIgnoreCase)
            || text.Contains("선관위", StringComparison.OrdinalIgnoreCase)
            || text.Contains("참정권", StringComparison.OrdinalIgnoreCase);
        if (!hasVotingSubject) return false;

        return text.Contains("부족", StringComparison.OrdinalIgnoreCase)
            || text.Contains("사태", StringComparison.OrdinalIgnoreCase)
            || text.Contains("재선거", StringComparison.OrdinalIgnoreCase)
            || text.Contains("봉쇄", StringComparison.OrdinalIgnoreCase)
            || text.Contains("잠실", StringComparison.OrdinalIgnoreCase)
            || text.Contains("송파", StringComparison.OrdinalIgnoreCase)
            || text.Contains("검경", StringComparison.OrdinalIgnoreCase)
            || text.Contains("법원", StringComparison.OrdinalIgnoreCase)
            || text.Contains("수사", StringComparison.OrdinalIgnoreCase)
            || text.Contains("증거보전", StringComparison.OrdinalIgnoreCase);
    }

    private static List<SummaryCandidate[]> BuildRelatedCandidateComponents(IReadOnlyList<SummaryCandidate> candidates)
    {
        var candidateKeywords = candidates
            .Select(candidate => SignificantRollupKeywords(candidate).ToArray())
            .ToArray();
        var keywordPairToIndexes = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < candidateKeywords.Length; index++)
        {
            var keywords = candidateKeywords[index].Take(8).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            for (var left = 0; left < keywords.Length - 1; left++)
            {
                for (var right = left + 1; right < keywords.Length; right++)
                {
                    var key = $"{keywords[left]}\u001f{keywords[right]}";
                    if (!keywordPairToIndexes.TryGetValue(key, out var indexes))
                    {
                        indexes = [];
                        keywordPairToIndexes[key] = indexes;
                    }

                    indexes.Add(index);
                }
            }
        }

        var maxKeywordDocumentCount = Math.Min(300, Math.Max(30, candidates.Count / 8));

        return keywordPairToIndexes.Values
            .Where(indexes => indexes.Count >= 3 && indexes.Count <= maxKeywordDocumentCount)
            .Select(indexes => indexes
                .Distinct()
                .Select(index => candidates[index])
                .ToArray())
            .Where(group => group.Length > 1)
            .GroupBy(group => string.Join(',', group.Select(candidate => candidate.Index).Order()))
            .Select(group => group.First())
            .ToList();
    }

    private static int RollupPriority(IReadOnlyCollection<SummaryCandidate> candidates)
    {
        var articleCount = candidates
            .SelectMany(candidate => candidate.EffectiveArticleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var sourceCount = candidates
            .SelectMany(candidate => candidate.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var bestSelectionScore = candidates.Max(candidate => candidate.SelectionScore);

        return Math.Min(600, candidates.Count * 6)
            + Math.Min(200, articleCount * 2)
            + Math.Min(120, sourceCount * 12)
            + Math.Min(120, bestSelectionScore);
    }

    private static bool ShouldCreateRollup(IReadOnlyCollection<SummaryCandidate> candidates, int totalCandidateCount)
    {
        if (candidates.Count < 3) return false;
        if (candidates.Count > Math.Max(250, totalCandidateCount / 8)) return false;
        if (candidates.All(candidate => IsLowBriefingValueGroup(candidate.Group))) return false;

        var articleCount = candidates
            .SelectMany(candidate => candidate.EffectiveArticleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var sourceCount = candidates
            .SelectMany(candidate => candidate.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return candidates.Count >= 5 || articleCount >= 5 || sourceCount >= 3;
    }

    private static SummaryIssueCandidate ToSingleIssueCandidate(SummaryCandidate candidate)
    {
        return new SummaryIssueCandidate
        {
            Title = Clean(candidate.Group.RepresentativeTitle),
            Category = candidate.Group.Category,
            Summary = Clean(candidate.Group.Summary),
            EffectiveArticleCount = candidate.EffectiveArticleCount,
            EffectiveArticleIds = candidate.EffectiveArticleIds,
            Sources = candidate.Sources.Take(4).ToArray(),
            AllSources = candidate.Sources,
            Score = candidate.EffectiveScore,
            SelectionScore = candidate.SelectionScore,
            Keywords = candidate.Keywords,
            EvidenceArticles = BuildEvidenceArticles(candidate.Articles),
            LatestPublishedAt = candidate.Group.LatestPublishedAt,
            IsLowBriefingValue = IsLowBriefingValueGroup(candidate.Group),
            ComponentCount = 1
        };
    }

    private static SummaryIssueCandidate ToMergedIssueCandidate(IReadOnlyCollection<SummaryCandidate> candidates)
    {
        var representative = candidates
            .OrderBy(candidate => IsLowBriefingValueGroup(candidate.Group) ? 1 : 0)
            .ThenByDescending(candidate => candidate.SelectionScore)
            .ThenByDescending(candidate => candidate.Sources.Length)
            .ThenByDescending(candidate => candidate.EffectiveArticleCount)
            .ThenByDescending(candidate => candidate.Group.LatestPublishedAt)
            .First();
        var category = candidates
            .GroupBy(candidate => candidate.Group.Category)
            .OrderByDescending(group => group.Sum(candidate => candidate.SelectionScore))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .First()
            .Key;
        var articleIds = candidates
            .SelectMany(candidate => candidate.EffectiveArticleIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = candidates
            .SelectMany(candidate => candidate.Sources)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var keywordWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in candidates.SelectMany(candidate => candidate.KeywordWeights))
        {
            keywordWeights[keyword.Key] = keywordWeights.TryGetValue(keyword.Key, out var current)
                ? current + keyword.Value
                : keyword.Value;
        }

        var keywords = TopRollupKeywords(keywordWeights, 8);
        var articleCount = articleIds.Length > 0
            ? articleIds.Length
            : candidates.Sum(candidate => candidate.EffectiveArticleCount);
        var score = Math.Min(
            100,
            IssueSignalCalculator.CalculateImpact(articleCount, sources.Length, representative.Group.RepresentativeTitle, sources)
            + Math.Min(20, (candidates.Count - 1) * 3));
        var selectionScore = representative.SelectionScore
            + Math.Min(120, candidates.Count * 6)
            + Math.Min(80, articleCount * 2)
            + Math.Min(60, sources.Length * 8);
        var relatedTitle = BuildMergedIssueTitle(representative, keywords);
        var summary = BuildMergedIssueSummary(relatedTitle, candidates.Count, keywords);
        var evidenceArticles = candidates
            .SelectMany(candidate => candidate.Articles)
            .GroupBy(article => article.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new SummaryIssueCandidate
        {
            Title = relatedTitle,
            Category = category,
            Summary = summary,
            EffectiveArticleCount = articleCount,
            EffectiveArticleIds = articleIds,
            Sources = sources.Take(4).ToArray(),
            AllSources = sources,
            Score = score,
            SelectionScore = selectionScore,
            Keywords = keywords,
            EvidenceArticles = BuildEvidenceArticles(evidenceArticles),
            LatestPublishedAt = candidates.Max(candidate => candidate.Group.LatestPublishedAt),
            IsLowBriefingValue = candidates.All(candidate => IsLowBriefingValueGroup(candidate.Group)),
            ComponentCount = candidates.Count
        };
    }

    private static string BuildMergedIssueTitle(SummaryCandidate representative, IReadOnlyList<string> keywords)
    {
        var title = Clean(representative.Group.RepresentativeTitle);
        if (!string.IsNullOrWhiteSpace(title)) return title;

        var labelKeywords = keywords.Take(3).ToArray();
        return labelKeywords.Length == 0
            ? "관련 이슈"
            : $"{string.Join(' ', labelKeywords)} 관련 이슈";
    }

    private static string BuildMergedIssueSummary(string title, int issueCount, IReadOnlyList<string> keywords)
    {
        var keywordText = keywords.Count == 0
            ? "반복 키워드는 아직 충분하지 않습니다"
            : $"반복 키워드는 {string.Join(", ", keywords.Take(6))}입니다";
        return $"{title}을 포함해 같은 사건 흐름으로 보이는 {issueCount}개 이슈가 함께 확인됐습니다. {keywordText}.";
    }


}
