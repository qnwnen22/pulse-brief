using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class AdminOperationsController(
    NewsPipeline pipeline,
    OperationalLogService operationalLog,
    AdminAuthService adminAuth,
    IConfiguration configuration,
    ArticleMaintenanceService maintenance,
    OperationalDiagnosticsService diagnostics,
    ApplicationLifetimeInfo lifetime) : ApiControllerBase
{
    [HttpPost("/api/refresh")]
    public async Task<IResult> Refresh(CancellationToken cancellationToken)
    {
        if (!adminAuth.IsAuthenticated(HttpContext)) return AdminAuthService.AdminRequired();
        if (!adminAuth.HasValidCsrf(HttpContext)) return AdminAuthService.CsrfRequired();
        if (!configuration.GetValue("Collector:AllowWebManualRefresh", false))
        {
            return Results.Json(
                new
                {
                    error = "collector_separated",
                    message = "RSS 수집은 PulseBrief.Collector에서 실행됩니다."
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        await operationalLog.RecordAsync("info", "manual_refresh_requested", "Manual refresh was requested by an administrator.", cancellationToken: cancellationToken);
        var result = await pipeline.RunAsync(cancellationToken);
        return Results.Ok(result);
    }

    [HttpPost("/api/admin/fetch-missing-content")]
    public async Task<IResult> FetchMissingContent(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!adminAuth.IsAuthenticated(HttpContext)) return AdminAuthService.AdminRequired();
        if (!adminAuth.HasValidCsrf(HttpContext)) return AdminAuthService.CsrfRequired();
        return Results.Ok(await maintenance.FetchContentAsync(limit, cancellationToken));
    }

    [HttpPost("/api/admin/fetch-missing-images")]
    public async Task<IResult> FetchMissingImages(
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!adminAuth.IsAuthenticated(HttpContext)) return AdminAuthService.AdminRequired();
        if (!adminAuth.HasValidCsrf(HttpContext)) return AdminAuthService.CsrfRequired();
        return Results.Ok(await maintenance.FetchImagesAsync(limit, cancellationToken));
    }

    [HttpGet("/api/admin/diagnostics")]
    public async Task<IResult> Diagnostics()
    {
        if (!adminAuth.IsAuthenticated(HttpContext)) return AdminAuthService.AdminRequired();

        return Results.Ok(await diagnostics.BuildAsync(lifetime.StartedAt));
    }

    [HttpGet("/api/admin/dashboard")]
    public async Task<IResult> Dashboard()
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: false, out var denied)) return denied;

        return Results.Ok(await diagnostics.BuildAsync(lifetime.StartedAt));
    }

    [HttpPost("/api/admin/refresh")]
    public async Task<IResult> AdminRefresh(CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;
        if (!configuration.GetValue("Collector:AllowWebManualRefresh", false))
        {
            return Results.Json(
                new
                {
                    error = "collector_separated",
                    message = "RSS 수집은 PulseBrief.Collector에서 실행됩니다."
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        await operationalLog.RecordAsync("info", "manual_refresh_requested", "Manual refresh was requested from admin console.", cancellationToken: cancellationToken);
        var result = await pipeline.RunAsync(cancellationToken);
        return Results.Ok(result);
    }
}
