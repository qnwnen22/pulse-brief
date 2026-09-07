namespace PulseBrief;

public sealed partial class AdminContentService(IArticleStore store, OperationalLogService operationalLog)
{
    private static readonly string[] KnownCategories =
    [
        "정치/정책",
        "경제/산업",
        "사회",
        "국제",
        "IT/과학",
        "문화/연예",
        "스포츠",
        "생활/건강",
        "지역"
    ];

    private static object BuildArticleSearchResult(IReadOnlyList<Article> articles, IReadOnlyList<ArticleGroup> groups, AdminArticleQuery query)
    {
        var normalizedQuery = TextCleaner.Clean(query.Query).Trim();
        var groupByArticleId = BuildGroupByArticleId(groups);
        var categories = groups
            .Select(group => group.Category)
            .Concat(KnownCategories)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sources = articles
            .Select(article => TextCleaner.Clean(article.Source))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var filtered = articles
            .Where(article => MatchesArticle(article, groupByArticleId.GetValueOrDefault(article.Id), query, normalizedQuery))
            .OrderByDescending(article => article.PublishedAt)
            .ThenByDescending(article => article.FirstSeenAt)
            .ToArray();
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var page = Math.Max(1, query.Page);
        var pageCount = Math.Max(1, (int)Math.Ceiling((double)filtered.Length / pageSize));
        if (page > pageCount) page = pageCount;

        return new
        {
            page,
            pageSize,
            pageCount,
            totalCount = filtered.Length,
            categories,
            sources,
            items = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(article => ToAdminArticleListItem(article, groupByArticleId.GetValueOrDefault(article.Id)))
                .ToArray()
        };
    }

    private static bool MatchesArticle(Article article, ArticleGroup? group, AdminArticleQuery query, string normalizedQuery)
    {
        if (query.Excluded is not null && article.IsExcluded != query.Excluded.Value) return false;

        if (!string.IsNullOrWhiteSpace(query.Category)
            && !string.Equals(group?.Category ?? "", query.Category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Source)
            && !string.Equals(TextCleaner.Clean(article.Source), query.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ContentStatus))
        {
            var status = string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus;
            if (!string.Equals(status, query.ContentStatus, StringComparison.OrdinalIgnoreCase)) return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedQuery)) return true;

        var haystack = TextCleaner.Clean($"{article.Title} {article.Source} {article.Author} {article.Url} {article.Summary} {article.FeedUrl}");
        return haystack.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static object ToAdminArticleListItem(Article article, ArticleGroup? group)
    {
        return new
        {
            article.Id,
            title = TextCleaner.Clean(article.Title),
            source = TextCleaner.Clean(article.Source),
            author = TextCleaner.Clean(article.Author),
            article.Url,
            article.FeedUrl,
            groupId = group?.Id ?? "",
            category = group?.Category ?? "미분류",
            isExcluded = article.IsExcluded,
            publishedAt = article.PublishedAt,
            updatedAt = article.UpdatedAt,
            contentFetchStatus = string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus,
            contentFetchError = TextCleaner.Clean(article.ContentFetchError),
            hasContent = !string.IsNullOrWhiteSpace(article.Content),
            hasImage = !string.IsNullOrWhiteSpace(article.ImageUrl),
            summaryPreview = Preview(article.Summary, 180),
            contentPreview = Preview(article.Content, 220)
        };
    }

    private static object ToAdminArticleDetail(Article article, ArticleGroup? group)
    {
        return new
        {
            article.Id,
            title = TextCleaner.Clean(article.Title),
            source = TextCleaner.Clean(article.Source),
            author = TextCleaner.Clean(article.Author),
            article.Url,
            article.FeedUrl,
            groupId = group?.Id ?? "",
            category = group?.Category ?? "미분류",
            isExcluded = article.IsExcluded,
            summary = TextCleaner.Clean(article.Summary),
            content = TextCleaner.Clean(article.Content),
            imageUrl = article.ImageUrl,
            publishedAt = article.PublishedAt,
            firstSeenAt = article.FirstSeenAt,
            updatedAt = article.UpdatedAt,
            contentFetchedAt = article.ContentFetchedAt,
            contentFetchStatus = string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus,
            contentFetchError = TextCleaner.Clean(article.ContentFetchError),
            group = group is null ? null : new
            {
                group.Id,
                group.Category,
                title = TextCleaner.Clean(group.RepresentativeTitle),
                summary = TextCleaner.Clean(group.Summary),
                group.ArticleCount,
                group.Score
            }
        };
    }

    private static Dictionary<string, ArticleGroup> BuildGroupByArticleId(IEnumerable<ArticleGroup> groups)
    {
        var result = new Dictionary<string, ArticleGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var articleId in group.ArticleIds)
            {
                result.TryAdd(articleId, group);
            }
        }

        return result;
    }

    private static void ApplyArticleUpdate(Article article, AdminArticleUpdateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Title)) article.Title = TextCleaner.Clean(request.Title);
        if (!string.IsNullOrWhiteSpace(request.Source)) article.Source = TextCleaner.Clean(request.Source);
        if (request.Author is not null) article.Author = TextCleaner.Clean(request.Author);
        if (request.Summary is not null) article.Summary = TextCleaner.Clean(request.Summary);
        if (request.IsExcluded is not null) article.IsExcluded = request.IsExcluded.Value;
        article.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static ArticleGroup? ApplyGroupCategoryForArticle(IEnumerable<ArticleGroup> groups, string articleId, string? category)
    {
        var group = groups.FirstOrDefault(item => item.ArticleIds.Contains(articleId, StringComparer.OrdinalIgnoreCase));
        if (group is null || string.IsNullOrWhiteSpace(category)) return group;

        group.Category = TextCleaner.Clean(category);
        return group;
    }

    private static void ApplyGroupUpdate(ArticleGroup group, AdminGroupUpdateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Category)) group.Category = TextCleaner.Clean(request.Category);
        if (!string.IsNullOrWhiteSpace(request.RepresentativeTitle)) group.RepresentativeTitle = TextCleaner.Clean(request.RepresentativeTitle);
        if (request.Summary is not null) group.Summary = TextCleaner.Clean(request.Summary);
    }

    private static ArticleGroup[] RemoveExcludedArticlesFromGroups(IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var articleById = articles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);
        return groups
            .Select(group =>
            {
                var includedIds = group.ArticleIds
                    .Where(id => articleById.TryGetValue(id, out var article) && !article.IsExcluded)
                    .ToArray();
                var includedArticles = includedIds
                    .Select(id => articleById[id])
                    .OrderByDescending(article => article.PublishedAt)
                    .ToArray();

                group.ArticleIds = includedIds;
                group.ArticleCount = includedIds.Length;
                group.Sources = includedArticles
                    .Select(article => TextCleaner.Clean(article.Source))
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                group.LatestPublishedAt = includedArticles.FirstOrDefault()?.PublishedAt ?? group.LatestPublishedAt;
                return group;
            })
            .Where(group => group.ArticleIds.Length > 0)
            .ToArray();
    }

    private static string Preview(string value, int maxLength)
    {
        var text = TextCleaner.Clean(value).Trim();
        if (text.Length <= maxLength) return text;
        return $"{text[..maxLength].Trim()}...";
    }
}
