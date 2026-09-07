namespace PulseBrief;

/// <summary>관리자의 기사 조회, 수정과 이슈 그룹 보정을 처리합니다.</summary>
public sealed partial class AdminContentService
{
    public async Task<object> SearchAsync(AdminArticleQuery query)
    {
        var articles = await store.ReadArticlesAsync();
        var groups = await store.ReadGroupsAsync();
        return BuildArticleSearchResult(articles, groups, query);
    }

    public async Task<object?> ReadAsync(string id)
    {
        var articles = await store.ReadArticlesAsync();
        var groups = await store.ReadGroupsAsync();
        var article = articles.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        return article is null ? null : ToAdminArticleDetail(article, groups.FirstOrDefault(group => group.ArticleIds.Contains(article.Id, StringComparer.OrdinalIgnoreCase)));
    }

    public async Task<object?> UpdateArticleAsync(string id, AdminArticleUpdateRequest request, CancellationToken cancellationToken)
    {
        var articles = await store.ReadArticlesAsync();
        var groups = await store.ReadGroupsAsync();
        var article = articles.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (article is null) return null;
        ApplyArticleUpdate(article, request);
        var changedGroup = ApplyGroupCategoryForArticle(groups, article.Id, request.Category);
        await store.SaveArticlesAsync(articles);
        await store.SaveGroupsAsync(RemoveExcludedArticlesFromGroups(groups, articles));
        await operationalLog.RecordAsync("info", "admin_article_updated", "Article metadata was updated from admin console.", new
        {
            article.Id, article.Title, article.IsExcluded, category = changedGroup?.Category
        }, cancellationToken);
        return ToAdminArticleDetail(article, changedGroup);
    }

    public async Task<ArticleGroup?> UpdateGroupAsync(string id, AdminGroupUpdateRequest request, CancellationToken cancellationToken)
    {
        var groups = await store.ReadGroupsAsync();
        var group = groups.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (group is null) return null;
        ApplyGroupUpdate(group, request);
        await store.SaveGroupsAsync(groups);
        await operationalLog.RecordAsync("info", "admin_group_updated", "Issue group metadata was updated from admin console.", new
        {
            group.Id, group.Category, group.RepresentativeTitle
        }, cancellationToken);
        return group;
    }
}
