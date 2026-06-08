using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PulseBrief;

var builder = WebApplication.CreateBuilder(args);
DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddHttpClient<RssCollector>();
builder.Services.AddSingleton<AppPaths>();
builder.Services.AddSingleton<MongoArticleStore>();
builder.Services.AddSingleton<IArticleStore>(services => services.GetRequiredService<MongoArticleStore>());
builder.Services.AddHttpClient<ArticleContentFetcher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("ArticleContent:TimeoutSeconds", 15));
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false
});
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<ArticleClusterer>();
builder.Services.AddSingleton<BriefGenerator>();
builder.Services.AddHttpClient<OpenAiDailySummaryClient>();
builder.Services.AddSingleton<DailySummaryService>();
builder.Services.AddSingleton<NewsPipeline>();
builder.Services.AddHostedService<ScheduledRefreshService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (HttpContext context, AppPaths paths, IConfiguration configuration) =>
{
    var feeds = await paths.ReadFeedUrlsAsync();
    var isAdmin = IsAdminRequest(context, configuration);
    return Results.Ok(new
    {
        ok = true,
        server = ".NET",
        database = configuration["Storage:Provider"] ?? "MongoDB",
        rssFeedCount = feeds.Count,
        hasOpenAiKey = isAdmin
            ? !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
            : (bool?)null
    });
});

app.MapGet("/api/articles", async (HttpContext context, IArticleStore store, IConfiguration configuration) =>
{
    if (!IsAdminRequest(context, configuration)) return AdminRequired();
    return Results.Ok(await store.ReadArticlesAsync());
});

app.MapGet("/api/groups", async (HttpContext context, IArticleStore store, IConfiguration configuration) =>
{
    if (!IsAdminRequest(context, configuration)) return AdminRequired();
    return Results.Ok(await store.ReadGroupsAsync());
});

app.MapGet("/api/briefs", async (IArticleStore store) =>
{
    var articles = await store.ReadArticlesAsync();
    var groups = await store.ReadGroupsAsync();
    return Results.Ok(ApiMapper.ToBriefs(groups, articles));
});

app.MapGet("/api/daily-summary", async (HttpContext context, string? date, bool? force, DailySummaryService dailySummaryService, CancellationToken cancellationToken) =>
{
    try
    {
        if ((force.GetValueOrDefault() || !string.IsNullOrWhiteSpace(date)) && !IsAdminRequest(context, app.Configuration))
        {
            return AdminRequired();
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

        return Results.Ok(await dailySummaryService.GetOrCreateSummaryAsync(targetDate, force.GetValueOrDefault(), cancellationToken));
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
        Console.WriteLine($"[daily-summary] failed: {error}");
        return Results.Json(CreateLocalError(context, error), statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/weekly-summary", async (HttpContext context, string? endDate, bool? force, DailySummaryService dailySummaryService, CancellationToken cancellationToken) =>
{
    try
    {
        if ((force.GetValueOrDefault() || !string.IsNullOrWhiteSpace(endDate)) && !IsAdminRequest(context, app.Configuration))
        {
            return AdminRequired();
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

        return Results.Ok(await dailySummaryService.GetOrCreateWeeklySummaryAsync(targetEndDate, force.GetValueOrDefault(), cancellationToken));
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
        Console.WriteLine($"[weekly-summary] failed: {error}");
        return Results.Json(CreateLocalError(context, error), statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/refresh", async (HttpContext context, NewsPipeline pipeline, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!IsAdminRequest(context, configuration)) return AdminRequired();

    var result = await pipeline.RunAsync(cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/admin/fetch-missing-content", async (HttpContext context, int? limit, IArticleStore store, ArticleContentFetcher contentFetcher, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!IsAdminRequest(context, configuration)) return AdminRequired();

    var articles = await store.ReadArticlesAsync();
    var targetLimit = Math.Clamp(limit.GetValueOrDefault(500), 1, 2000);
    var beforeMissing = articles.Count(article => string.IsNullOrWhiteSpace(article.ContentFetchStatus));

    await contentFetcher.EnrichMissingContentAsync(articles, targetLimit, cancellationToken);
    await store.SaveArticlesAsync(articles);

    var statusCounts = articles
        .GroupBy(article => string.IsNullOrWhiteSpace(article.ContentFetchStatus) ? "pending" : article.ContentFetchStatus)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

    return Results.Ok(new
    {
        requested = targetLimit,
        processed = Math.Min(targetLimit, beforeMissing),
        beforeMissing,
        afterMissing = statusCounts.GetValueOrDefault("pending"),
        success = statusCounts.GetValueOrDefault("success"),
        failed = statusCounts.GetValueOrDefault("failed")
    });
});

app.MapPost("/api/admin/fetch-missing-images", async (HttpContext context, int? limit, IArticleStore store, ArticleContentFetcher contentFetcher, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!IsAdminRequest(context, configuration)) return AdminRequired();

    var articles = await store.ReadArticlesAsync();
    var targetLimit = Math.Clamp(limit.GetValueOrDefault(500), 1, 2000);
    var beforeMissing = articles.Count(article => string.IsNullOrWhiteSpace(article.ImageUrl));

    await contentFetcher.EnrichMissingImagesAsync(articles, targetLimit, cancellationToken);
    await store.SaveArticlesAsync(articles);

    var afterMissing = articles.Count(article => string.IsNullOrWhiteSpace(article.ImageUrl));

    return Results.Ok(new
    {
        requested = targetLimit,
        processed = Math.Min(targetLimit, beforeMissing),
        beforeMissing,
        afterMissing,
        withImage = articles.Count - afterMissing
    });
});

app.MapFallbackToFile("index.html");

app.Run();

static object CreateLocalError(HttpContext context, Exception error)
{
    var isLoopback = context.Connection.RemoteIpAddress is { } remoteIpAddress && IPAddress.IsLoopback(remoteIpAddress);
    return isLoopback
        ? new { error = error.GetType().Name, message = error.Message }
        : new { error = "summary_failed" };
}

static IResult AdminRequired()
{
    return Results.Json(new { error = "admin_required" }, statusCode: StatusCodes.Status401Unauthorized);
}

static bool IsAdminRequest(HttpContext context, IConfiguration configuration)
{
    var configuredToken = GetAdminToken(configuration);
    if (!string.IsNullOrWhiteSpace(configuredToken))
    {
        var requestToken = context.Request.Headers["X-Admin-Token"].FirstOrDefault();
        if (TokenEquals(requestToken, configuredToken)) return true;
    }

    return configuration.GetValue("Security:AllowLoopbackAdmin", false)
        && context.Connection.RemoteIpAddress is { } remoteIpAddress
        && IPAddress.IsLoopback(remoteIpAddress);
}

static string? GetAdminToken(IConfiguration configuration)
{
    return Environment.GetEnvironmentVariable("PULSEBRIEF_ADMIN_TOKEN")
        ?? Environment.GetEnvironmentVariable("ADMIN_API_TOKEN")
        ?? configuration["Security:AdminToken"];
}

static bool TokenEquals(string? requestToken, string configuredToken)
{
    if (string.IsNullOrWhiteSpace(requestToken)) return false;

    var requestBytes = Encoding.UTF8.GetBytes(requestToken);
    var configuredBytes = Encoding.UTF8.GetBytes(configuredToken);
    return requestBytes.Length == configuredBytes.Length
        && CryptographicOperations.FixedTimeEquals(requestBytes, configuredBytes);
}
