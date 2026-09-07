using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class AdminSummariesController(
    DailySummaryService dailySummaryService,
    OperationalLogService operationalLog,
    AdminAuthService adminAuth) : ApiControllerBase
{
    [HttpPost("/api/admin/summaries/daily/regenerate")]
    public async Task<IResult> RegenerateDaily(
        [FromBody] AdminJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;
        if (!TryParseDate(request.Date, "date", out var date, out var error)) return error!;
        if (!dailySummaryService.IsGenerationEnabled) return SummaryGenerationDisabled();

        var summary = await dailySummaryService.GetOrCreateSummaryAsync(date, force: true, cancellationToken);
        await operationalLog.RecordAsync("info", "admin_daily_summary_regenerated", "Daily summary was regenerated from admin console.", new { summary.Date, summary.Provider, summary.Model }, cancellationToken);
        return Results.Ok(summary);
    }

    [HttpPost("/api/admin/summaries/daily/preview")]
    public async Task<IResult> PreviewDaily(
        [FromBody] AdminJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;
        if (!TryParseDate(request.Date, "date", out var date, out var error)) return error!;
        if (!dailySummaryService.IsGenerationEnabled) return SummaryGenerationDisabled();

        var summary = await dailySummaryService.GenerateSummaryPreviewAsync(date, cancellationToken);
        await operationalLog.RecordAsync("info", "admin_daily_summary_preview_generated", "Daily summary preview was generated without saving.", new { summary.Date, summary.Provider, summary.Model, saved = false }, cancellationToken);
        return Results.Ok(new { saved = false, summary });
    }

    [HttpPost("/api/admin/summaries/weekly/regenerate")]
    public async Task<IResult> RegenerateWeekly(
        [FromBody] AdminJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;
        if (!TryParseDate(request.EndDate, "endDate", out var endDate, out var error)) return error!;
        if (!dailySummaryService.IsGenerationEnabled) return SummaryGenerationDisabled();

        var summary = await dailySummaryService.GetOrCreateWeeklySummaryAsync(endDate, force: true, cancellationToken);
        await operationalLog.RecordAsync("info", "admin_weekly_summary_regenerated", "Weekly summary was regenerated from admin console.", new { summary.Date, summary.Provider, summary.Model }, cancellationToken);
        return Results.Ok(summary);
    }
}
