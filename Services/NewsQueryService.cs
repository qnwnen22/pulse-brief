namespace PulseBrief;

public sealed class NewsQueryService(IArticleStore store, AppPaths paths, IConfiguration configuration)
{
    public async Task<BriefDto[]> ReadBriefsAsync()
    {
        var maxBriefs = Math.Clamp(configuration.GetValue("PublicBriefs:MaxGroups", 600), 100, 2000);
        var groups = await store.ReadRecentGroupsAsync(maxBriefs * 2);
        var articleIds = groups
            .SelectMany(group => group.ArticleIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var articles = await store.ReadArticlesByIdsAsync(articleIds);
        var feedUrls = await paths.ReadFeedUrlsAsync();
        var activeFeeds = feedUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activePublishers = feedUrls
            .Select(feedUrl => RssSourceCatalog.SourceInfoForUrl(feedUrl).Publisher)
            .Where(publisher => !string.IsNullOrWhiteSpace(publisher))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (activeFeeds.Count > 0)
        {
            articles = articles
                .Where(article =>
                    activeFeeds.Contains(article.FeedUrl) ||
                    activePublishers.Contains(RssSourceCatalog.SourceInfoForUrl(article.FeedUrl).Publisher))
                .ToList();
        }

        return ApiMapper.ToBriefs(groups, articles).Take(maxBriefs).ToArray();
    }
}
