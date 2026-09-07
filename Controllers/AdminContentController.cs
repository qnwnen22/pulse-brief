using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class AdminContentController(
    IArticleStore store,
    AdminAuthService adminAuth,
    AdminContentService content) : ApiControllerBase
{
    [HttpGet("/api/articles")]
    public async Task<IResult> AllArticles()
    {
        if (!adminAuth.IsAuthenticated(HttpContext)) return AdminAuthService.AdminRequired();
        return Results.Ok(await store.ReadArticlesAsync());
    }

    [HttpGet("/api/groups")]
    public async Task<IResult> AllGroups()
    {
        if (!adminAuth.IsAuthenticated(HttpContext)) return AdminAuthService.AdminRequired();
        return Results.Ok(await store.ReadGroupsAsync());
    }

    [HttpGet("/api/admin/articles")]
    public async Task<IResult> SearchArticles(
        [FromQuery] string? query,
        [FromQuery] string? category,
        [FromQuery] string? source,
        [FromQuery] string? contentStatus,
        [FromQuery] bool? excluded,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: false, out var denied)) return denied;

        var adminQuery = new AdminArticleQuery
        {
            Query = query ?? "",
            Category = category ?? "",
            Source = source ?? "",
            ContentStatus = contentStatus ?? "",
            Excluded = excluded,
            Page = page.GetValueOrDefault(1),
            PageSize = pageSize.GetValueOrDefault(25)
        };

        return Results.Ok(await content.SearchAsync(adminQuery));
    }

    [HttpGet("/api/admin/articles/{id}")]
    public async Task<IResult> Article([FromRoute] string id)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: false, out var denied)) return denied;

        var result = await content.ReadAsync(id);
        return result is null ? Results.NotFound(new { error = "article_not_found" }) : Results.Ok(result);
    }

    [HttpPatch("/api/admin/articles/{id}")]
    public async Task<IResult> UpdateArticle(
        [FromRoute] string id,
        [FromBody] AdminArticleUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;

        var result = await content.UpdateArticleAsync(id, request, cancellationToken);
        return result is null ? Results.NotFound(new { error = "article_not_found" }) : Results.Ok(result);
    }

    [HttpPatch("/api/admin/groups/{id}")]
    public async Task<IResult> UpdateGroup(
        [FromRoute] string id,
        [FromBody] AdminGroupUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;

        var result = await content.UpdateGroupAsync(id, request, cancellationToken);
        return result is null ? Results.NotFound(new { error = "group_not_found" }) : Results.Ok(result);
    }
}
