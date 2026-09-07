using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class SummariesController(
    DailySummaryService dailySummaryService,
    AdminAuthService adminAuth,
    SummaryLinkService summaryLinks) : ApiControllerBase
{
    [HttpGet("/api/daily-summary")]
    public async Task<IResult> Daily(
        [FromQuery] string? date,
        [FromQuery] bool? force,
        CancellationToken cancellationToken)
    {
        try
        {
            if ((force.GetValueOrDefault() || !string.IsNullOrWhiteSpace(date)) && !adminAuth.IsAuthenticated(HttpContext))
            {
                return AdminAuthService.AdminRequired();
            }

            DateOnly? targetDate = null;
            if (!string.IsNullOrWhiteSpace(date))
            {
                if (!DateOnly.TryParse(date, out var parsed))
                {
                    return Results.BadRequest(new { error = "date must be yyyy-MM-dd" });
                }

                targetDate = parsed;
            }

            if (force.GetValueOrDefault() && !dailySummaryService.IsGenerationEnabled)
            {
                return Results.Json(
                    new { error = "summary_generation_disabled", message = "Summary generation is disabled." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var summary = force.GetValueOrDefault()
                ? await dailySummaryService.GetOrCreateSummaryAsync(targetDate, force: true, cancellationToken)
                : await dailySummaryService.GetStoredDailySummaryAsync(targetDate);

            return summary is null
                ? Results.NotFound(new { error = "daily_summary_not_ready" })
                : Results.Ok(await summaryLinks.AddLinksAsync(summary));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Console.WriteLine($"[daily-summary] failed: {error}");
            return Results.Json(CreateLocalError(HttpContext, error), statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("/api/weekly-summary")]
    public async Task<IResult> Weekly(
        [FromQuery] string? endDate,
        [FromQuery] bool? force,
        CancellationToken cancellationToken)
    {
        try
        {
            if ((force.GetValueOrDefault() || !string.IsNullOrWhiteSpace(endDate)) && !adminAuth.IsAuthenticated(HttpContext))
            {
                return AdminAuthService.AdminRequired();
            }

            DateOnly? targetEndDate = null;
            if (!string.IsNullOrWhiteSpace(endDate))
            {
                if (!DateOnly.TryParse(endDate, out var parsed))
                {
                    return Results.BadRequest(new { error = "endDate must be yyyy-MM-dd" });
                }

                targetEndDate = parsed;
            }

            if (force.GetValueOrDefault() && !dailySummaryService.IsGenerationEnabled)
            {
                return Results.Json(
                    new { error = "summary_generation_disabled", message = "Summary generation is disabled." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var summary = force.GetValueOrDefault()
                ? await dailySummaryService.GetOrCreateWeeklySummaryAsync(targetEndDate, force: true, cancellationToken)
                : await dailySummaryService.GetStoredWeeklySummaryAsync(targetEndDate);

            return summary is null
                ? Results.NotFound(new { error = "weekly_summary_not_ready" })
                : Results.Ok(await summaryLinks.AddLinksAsync(summary));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Console.WriteLine($"[weekly-summary] failed: {error}");
            return Results.Json(CreateLocalError(HttpContext, error), statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
