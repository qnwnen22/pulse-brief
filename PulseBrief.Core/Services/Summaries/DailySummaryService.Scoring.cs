using System.Text.RegularExpressions;

namespace PulseBrief;

public sealed partial class DailySummaryService
{
    private static SummaryCandidate BuildCandidate(ArticleGroup group, int index, IReadOnlyDictionary<string, Article> articleById)
    {
        var articles = ArticleDedupe.EffectiveArticles(group.ArticleIds.Select(id => articleById.TryGetValue(id, out var article) ? article : null));
        var effectiveIds = articles.Select(article => article.Id).ToArray();
        var sources = articles
            .Select(article => Clean(article.Source))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
        {
            sources = group.Sources
                .Select(Clean)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var keywordWeights = ExtractKeywordWeights(group, articles);
        return new SummaryCandidate
        {
            Index = index,
            Group = group,
            Articles = articles,
            EffectiveArticleIds = effectiveIds,
            Sources = sources,
            EffectiveScore = EffectiveScore(group, articleById),
            EffectiveArticleCount = effectiveIds.Length > 0 ? effectiveIds.Length : group.ArticleCount,
            KeywordWeights = keywordWeights,
            Keywords = TopKeywords(keywordWeights, 8)
        };
    }

    private static void ApplyKeywordDistributionScores(IReadOnlyCollection<SummaryCandidate> candidates)
    {
        var distributions = candidates
            .GroupBy(candidate => candidate.Group.Category)
            .ToDictionary(
                group => group.Key,
                group => group
                    .SelectMany(candidate => candidate.KeywordWeights)
                    .GroupBy(keyword => keyword.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(keywordGroup => keywordGroup.Key, keywordGroup => keywordGroup.Sum(keyword => keyword.Value), StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            distributions.TryGetValue(candidate.Group.Category, out var categoryDistribution);
            var keywordScore = candidate.Keywords
                .Take(6)
                .Sum(keyword => categoryDistribution?.GetValueOrDefault(keyword) ?? 0);
            candidate.KeywordDistributionScore = keywordScore;
            candidate.SelectionScore = candidate.EffectiveScore
                + Math.Min(40, (int)Math.Round(Math.Sqrt(keywordScore) * 4))
                + Math.Min(20, candidate.Sources.Length * 4)
                + Math.Min(15, candidate.EffectiveArticleCount * 2);
        }
    }

    private static DailyTopIssue ToDailyTopIssue(SummaryIssueCandidate candidate)
    {
        return new DailyTopIssue
        {
            Title = candidate.Title,
            Category = candidate.Category,
            Summary = candidate.Summary,
            ArticleCount = candidate.EffectiveArticleCount,
            ArticleIds = candidate.EffectiveArticleIds,
            Score = candidate.Score,
            Sources = candidate.Sources,
            Keywords = candidate.Keywords,
            EvidenceArticles = candidate.EvidenceArticles
        };
    }

    /// <summary>포토/화보처럼 사실 흐름 요약 가치가 낮은 이슈를 대표 요약 후보에서 후순위로 밀기 위해 판별합니다.</summary>
    private static bool IsLowBriefingValueGroup(ArticleGroup group)
    {
        return IssueSignalCalculator.IsLowBriefingValue($"{group.RepresentativeTitle} {group.SeedTitle}", group.Sources);
    }

    /// <summary>요약 정렬과 노출에 사용할 중복 제거 기준의 중요도 점수를 계산합니다.</summary>
    private static int EffectiveScore(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        var effectiveArticleCount = ArticleDedupe.EffectiveArticleCount(group, articleById);
        var sources = EffectiveSources(group, articleById);
        var articleCount = effectiveArticleCount > 0 ? effectiveArticleCount : group.ArticleCount;
        return IssueSignalCalculator.CalculateImpact(articleCount, sources.Length, Clean(group.RepresentativeTitle), sources);
    }

    private static Dictionary<string, int> ExtractKeywordWeights(ArticleGroup group, IReadOnlyList<Article> articles)
    {
        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        AddKeywordWeights(weights, group.RepresentativeTitle, 4);
        AddKeywordWeights(weights, group.SeedTitle, 3);
        AddKeywordWeights(weights, group.Summary, 2);

        foreach (var article in articles.Take(8))
        {
            AddKeywordWeights(weights, article.Title, 3);
            AddKeywordWeights(weights, article.Summary, 2);
            AddKeywordWeights(weights, Compact(article.Content, 2500), 1);
        }

        return weights;
    }

    private static void AddKeywordWeights(IDictionary<string, int> weights, string? text, int weight)
    {
        foreach (Match match in KeywordRegex.Matches(Clean(text ?? "")))
        {
            var keyword = match.Value.ToLowerInvariant();
            if (!IsKeywordCandidate(keyword)) continue;
            weights[keyword] = weights.TryGetValue(keyword, out var current)
                ? current + weight
                : weight;
        }
    }

    private static bool IsKeywordCandidate(string keyword)
    {
        if (keyword.Length < 2 || keyword.Length > 30) return false;
        if (keyword.All(char.IsDigit)) return false;
        if (KeywordStopwords.Contains(keyword)) return false;
        if (keyword.EndsWith("기자", StringComparison.OrdinalIgnoreCase) && keyword.Length <= 5) return false;
        return true;
    }

    private static string[] TopKeywords(IReadOnlyDictionary<string, int> weights, int count)
    {
        return weights
            .OrderByDescending(keyword => keyword.Value)
            .ThenBy(keyword => keyword.Key)
            .Take(count)
            .Select(keyword => keyword.Key)
            .ToArray();
    }

    private static IEnumerable<string> SignificantRollupKeywords(SummaryCandidate candidate)
    {
        return candidate.KeywordWeights
            .Where(keyword => IsRollupKeyword(keyword.Key))
            .OrderByDescending(keyword => keyword.Value)
            .ThenBy(keyword => keyword.Key)
            .Take(12)
            .Select(keyword => keyword.Key);
    }

    private static bool IsRollupKeyword(string keyword)
    {
        if (!IsKeywordCandidate(keyword)) return false;
        if (DateLikeKeywordRegex.IsMatch(keyword)) return false;
        if (keyword.Any(char.IsDigit) && keyword.Length <= 4) return false;
        if (keyword.All(character => character < 128) && keyword.Length <= 3) return false;
        if (RollupKeywordStopwords.Contains(keyword)) return false;
        return keyword.Length >= 2;
    }

    private static string[] TopRollupKeywords(IReadOnlyDictionary<string, int> weights, int count)
    {
        return weights
            .Where(keyword => IsRollupKeyword(keyword.Key))
            .OrderByDescending(keyword => keyword.Value)
            .ThenBy(keyword => keyword.Key)
            .Take(count)
            .Select(keyword => keyword.Key)
            .ToArray();
    }

    private static DailyIssueEvidenceArticle[] BuildEvidenceArticles(IReadOnlyCollection<Article> articles)
    {
        return articles
            .OrderBy(article => string.IsNullOrWhiteSpace(article.Content) ? 1 : 0)
            .ThenByDescending(article => article.PublishedAt)
            .Take(2)
            .Select(article => new DailyIssueEvidenceArticle
            {
                Title = Clean(article.Title),
                Source = Clean(article.Source),
                Summary = Compact(article.Summary, 350),
                ContentExcerpt = Compact(article.Content, 1000),
                Url = article.Url
            })
            .ToArray();
    }

    /// <summary>중복 제거 후 남은 기사에서 출처를 추출하고, 기사 문서가 없으면 그룹의 저장 출처를 사용합니다.</summary>
    private static string[] EffectiveSources(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        var sources = ArticleDedupe.EffectiveArticles(group.ArticleIds.Select(id => articleById.TryGetValue(id, out var article) ? article : null))
            .Select(article => Clean(article.Source))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return sources.Length > 0
            ? sources
            : group.Sources.Select(Clean).Where(source => !string.IsNullOrWhiteSpace(source)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }


}
