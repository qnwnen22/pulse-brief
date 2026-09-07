namespace PulseBrief;

public sealed class SummaryLinkService(IArticleStore store)
{
    public async Task<DailyIssueSummary> AddLinksAsync(DailyIssueSummary summary)
    {
        // Bound indexed lookups; ReadArticlesByIdsAsync excludes content and embeddings.
        var articleIds = summary.TopIssues
            .SelectMany(issue => issue.ArticleIds.Take(100))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(1000)
            .ToArray();
        if (articleIds.Length == 0) return summary;

        try
        {
            var articles = await store.ReadArticlesByIdsAsync(articleIds);
            var byId = articles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var issue in summary.TopIssues)
            {
                issue.RelatedLinks = ArticleDedupe.EffectiveArticles(
                    issue.ArticleIds.Take(100).Select(id => byId.GetValueOrDefault(id)))
                    .Where(article => !string.IsNullOrWhiteSpace(article.Url))
                    .DistinctBy(article => article.Url)
                    .Select(article => new RelatedLinkDto
                    {
                        Title = TextCleaner.Clean(article.Title),
                        Source = TextCleaner.Clean(article.Source),
                        Url = article.Url
                    }).ToArray();
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"[summary-links] failed: {error.GetType().Name}");
        }

        return summary;
    }


}
