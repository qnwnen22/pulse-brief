using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class NewsController(
    IArticleStore store,
    NewsQueryService news) : ApiControllerBase
{
    [HttpGet("/api/news-stats")]
    public async Task<IResult> Stats()
    {
        var stats = await store.ReadNewsStatsAsync();
        return Results.Ok(stats ?? NewsStats.WaitingFor(KoreaDate.Today()));
    }

    [HttpGet("/api/briefs")]
    public async Task<IResult> Briefs()
    {
        return Results.Ok(await news.ReadBriefsAsync());
    }
}
