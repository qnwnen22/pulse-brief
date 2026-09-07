using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class HealthController(
    AppPaths paths,
    IConfiguration configuration,
    AdminAuthService adminAuth) : ApiControllerBase
{
    [HttpGet("/api/health")]
    public async Task<IResult> Get()
    {
        var feeds = await paths.ReadFeedUrlsAsync();
        var isAdmin = adminAuth.IsAuthenticated(HttpContext);
        return Results.Ok(new
        {
            ok = true,
            server = ".NET",
            version = AppVersion.Current,
            database = configuration["Storage:Provider"] ?? "MongoDB",
            rssFeedCount = feeds.Count,
            hasOpenAiKey = isAdmin
                ? !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                : (bool?)null
        });
    }
}
