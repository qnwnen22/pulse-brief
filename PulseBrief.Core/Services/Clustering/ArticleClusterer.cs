namespace PulseBrief;

/// <summary>기사 임베딩 유사도와 키워드 규칙을 사용해 유사 기사들을 이슈 그룹으로 묶습니다.</summary>
public sealed class ArticleClusterer(IConfiguration configuration)
{
    private readonly double _threshold = configuration.GetValue("GroupSimilarityThreshold", 0.78);

    /// <summary>기사 임베딩 간 코사인 유사도를 기준으로 기사 목록을 여러 이슈 그룹으로 분류합니다.</summary>
    public List<ArticleGroup> GroupSimilarArticles(IEnumerable<Article> articles)
    {
        var workingGroups = new List<WorkingGroup>();

        foreach (var article in articles.Where(article => !article.IsExcluded && article.Embedding is { Length: > 0 }))
        {
            WorkingGroup? bestGroup = null;
            var bestScore = 0.0;

            foreach (var group in workingGroups)
            {
                var score = CosineSimilarity(article.Embedding!, group.Centroid);
                if (score > bestScore)
                {
                    bestGroup = group;
                    bestScore = score;
                }
            }

            if (bestGroup is not null && bestScore >= _threshold)
            {
                bestGroup.Articles.Add(article);
                bestGroup.Centroid = Centroid(bestGroup.Articles);
            }
            else
            {
                workingGroups.Add(new WorkingGroup
                {
                    Centroid = article.Embedding!,
                    Articles = [article]
                });
            }
        }

        return workingGroups.Select((group, index) =>
        {
            var rawGroupArticles = group.Articles
                .OrderByDescending(article => article.PublishedAt)
                .ThenByDescending(article => article.FirstSeenAt)
                .ToArray();
            var groupArticles = ArticleDedupe.EffectiveArticles(rawGroupArticles);
            var articlesForDisplay = groupArticles.Length > 0 ? groupArticles : rawGroupArticles;
            var sources = articlesForDisplay
                .Select(article => article.Source)
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var seedTitle = articlesForDisplay.FirstOrDefault()?.Title ?? "새 이슈";
            var score = IssueSignalCalculator.CalculateImpact(articlesForDisplay.Length, sources.Length, seedTitle, sources);

            return new ArticleGroup
            {
                Id = $"group-{index + 1}",
                Category = ArticleCategoryClassifier.ForArticles(articlesForDisplay),
                ArticleIds = articlesForDisplay.Select(article => article.Id).ToArray(),
                ArticleCount = articlesForDisplay.Length,
                Sources = sources,
                LatestPublishedAt = articlesForDisplay.FirstOrDefault()?.PublishedAt ?? DateTimeOffset.UtcNow,
                Score = score,
                SeedTitle = seedTitle,
                SeedSummary = BestSummary(articlesForDisplay.FirstOrDefault())
            };
        }).ToList();
    }

    /// <summary>그룹에 포함된 기사 임베딩들의 평균 벡터를 계산합니다.</summary>
    private static double[] Centroid(IReadOnlyCollection<Article> articles)
    {
        var size = articles.FirstOrDefault()?.Embedding?.Length ?? 0;
        var vector = new double[size];
        if (size == 0) return vector;

        foreach (var article in articles)
        {
            for (var i = 0; i < size; i++) vector[i] += article.Embedding![i];
        }

        for (var i = 0; i < size; i++) vector[i] /= articles.Count;
        return vector;
    }

    /// <summary>그룹 초기 요약으로 사용할 수 있도록 추출 본문 일부를 우선하고 없으면 RSS 요약을 반환합니다.</summary>
    private static string BestSummary(Article? article)
    {
        if (article is null) return "";
        if (!string.IsNullOrWhiteSpace(article.Content)) return TextCleaner.Truncate(article.Content, 700);
        return TextCleaner.Truncate(article.Summary, 700);
    }

    /// <summary>두 임베딩 벡터의 코사인 유사도 점수를 계산합니다.</summary>
    private static double CosineSimilarity(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count != b.Count) return 0;

        var sum = 0.0;
        for (var i = 0; i < a.Count; i++) sum += a[i] * b[i];
        return sum;
    }

    /// <summary>그룹화 중간 단계에서 기사 목록과 중심 벡터를 함께 보관하는 작업용 모델입니다.</summary>
    private sealed class WorkingGroup
    {
        /// <summary>현재 작업 그룹의 평균 임베딩 벡터입니다.</summary>
        public double[] Centroid { get; set; } = [];

        /// <summary>현재 작업 그룹에 포함된 기사 목록입니다.</summary>
        public List<Article> Articles { get; set; } = [];
    }
}
