namespace PulseBrief;

public sealed class BriefGenerator
{
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
                ? firstArticle?.Summary ?? $"{group.ArticleCount} related articles were detected in this issue group."
                : group.Summary;
            return group;
        }).ToList();

        return Task.FromResult(enriched);
    }
}
