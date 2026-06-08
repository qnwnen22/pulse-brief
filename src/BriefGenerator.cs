namespace PulseBrief;

/// <summary>그룹화된 이슈에 대표 제목과 요약이 비어 있을 때 기본 값을 보강합니다.</summary>
public sealed class BriefGenerator
{
    /// <summary>기사 원본 데이터를 참고해 이슈 그룹의 대표 제목과 요약을 채웁니다.</summary>
    public Task<List<ArticleGroup>> EnrichGroupsAsync(IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var byId = articles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);

        var enriched = groups.Select(group =>
        {
            var firstArticle = group.ArticleIds.Select(id => byId.GetValueOrDefault(id)).FirstOrDefault(article => article is not null);
            group.RepresentativeTitle = string.IsNullOrWhiteSpace(group.RepresentativeTitle)
                ? group.SeedTitle
                : group.RepresentativeTitle;
            group.Summary = string.IsNullOrWhiteSpace(group.Summary)
                ? BestSummary(firstArticle) ?? $"{group.ArticleCount} related articles were detected in this issue group."
                : group.Summary;
            return group;
        }).ToList();

        return Task.FromResult(enriched);
    }

    /// <summary>추출 본문을 우선 사용하고 없으면 RSS 요약을 그룹 요약 후보로 반환합니다.</summary>
    private static string? BestSummary(Article? article)
    {
        if (article is null) return null;
        if (!string.IsNullOrWhiteSpace(article.Content))
        {
            return article.Content.Length > 900 ? article.Content[..900] : article.Content;
        }

        return article.Summary;
    }
}
