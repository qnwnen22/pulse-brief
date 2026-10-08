using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class StatisticsController(CollectionStatisticsService statistics, ILogger<StatisticsController> logger) : ControllerBase
{
    [HttpGet("/api/collection-statistics")]
    [ResponseCache(Duration = 60)]
    public async Task<IResult> Read([FromQuery] string period = "7", CancellationToken cancellationToken = default)
    {
        if (period is not ("7" or "30" or "all")) return Results.BadRequest(new { message = "지원하지 않는 통계 기간입니다." });
        try
        {
            return Results.Ok(await statistics.ReadAsync(period, KoreaDate.Today(), cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogWarning(error, "Cached collection statistics could not be read.");
            return Results.Json(new { message = "통계 데이터를 불러오지 못했습니다." }, statusCode: 503);
        }
    }
}
