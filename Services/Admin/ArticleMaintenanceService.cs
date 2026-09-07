namespace PulseBrief;

public sealed class ArticleMaintenanceService(IArticleStore store, ArticleContentFetcher contentFetcher)
{
    public async Task<object> FetchContentAsync(int? limit, CancellationToken cancellationToken)
    {
        var articles = await store.ReadArticlesAsync();
        var targetLimit = Math.Clamp(limit.GetValueOrDefault(500), 1, 2000);
        var beforeMissing = articles.Count(article => string.IsNullOrWhiteSpace(article.ContentFetchStatus));
        await contentFetcher.EnrichMissingContentAsync(articles, targetLimit, cancellationToken);
        await store.SaveArticlesAsync(articles);
        var statusCounts = articles
            .GroupBy(article => string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return new
        {
            requested = targetLimit,
            processed = Math.Min(targetLimit, beforeMissing),
            beforeMissing,
            afterMissing = statusCounts.GetValueOrDefault("pending"),
            success = statusCounts.GetValueOrDefault("success"),
            failed = statusCounts.GetValueOrDefault("failed")
        };
    }

    public async Task<object> FetchImagesAsync(int? limit, CancellationToken cancellationToken)
    {
        var articles = await store.ReadArticlesAsync();
        var targetLimit = Math.Clamp(limit.GetValueOrDefault(500), 1, 2000);
        var beforeMissing = articles.Count(article => string.IsNullOrWhiteSpace(article.ImageUrl));
        await contentFetcher.EnrichMissingImagesAsync(articles, targetLimit, cancellationToken);
        await store.SaveArticlesAsync(articles);
        var afterMissing = articles.Count(article => string.IsNullOrWhiteSpace(article.ImageUrl));
        return new { requested = targetLimit, processed = Math.Min(targetLimit, beforeMissing), beforeMissing, afterMissing, withImage = articles.Count - afterMissing };
    }
}
