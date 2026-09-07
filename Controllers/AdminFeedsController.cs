using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class AdminFeedsController(
    AdminAuthService adminAuth,
    AdminFeedService feeds) : ApiControllerBase
{
    [HttpGet("/api/admin/rss-feeds")]
    public async Task<IResult> List()
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: false, out var denied)) return denied;
        return Results.Ok(await feeds.ReadAsync());
    }

    [HttpPost("/api/admin/rss-feeds")]
    public async Task<IResult> Add(
        [FromBody] AdminRssFeedAddRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;
        if (!TryNormalizeUrl(request.Url, out var url)) return Results.BadRequest(new { error = "invalid_url" });
        if (!await feeds.AddAsync(url, request.IsActive, cancellationToken)) return Results.Conflict(new { error = "feed_already_exists" });
        return Results.Ok(new { ok = true, url, request.IsActive });
    }

    [HttpPatch("/api/admin/rss-feeds")]
    public async Task<IResult> Update(
        [FromBody] AdminRssFeedUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;
        if (!TryNormalizeUrl(request.Url, out var url)) return Results.BadRequest(new { error = "invalid_url" });
        if (!await feeds.UpdateAsync(url, request.IsActive, cancellationToken)) return Results.NotFound(new { error = "feed_not_found" });
        return Results.Ok(new { ok = true, url, request.IsActive });
    }

    [HttpPost("/api/admin/rss-feeds/remove")]
    public async Task<IResult> Remove(
        [FromBody] AdminRssFeedRemoveRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;
        if (!TryNormalizeUrl(request.Url, out var url)) return Results.BadRequest(new { error = "invalid_url" });
        if (!await feeds.RemoveAsync(url, cancellationToken)) return Results.NotFound(new { error = "feed_not_found" });
        return Results.Ok(new { ok = true, url });
    }
}
