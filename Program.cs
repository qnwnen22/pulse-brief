using System.Net;
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
builder.Services.AddSingleton<PipelineRunTracker>();
builder.Services.AddSingleton<OperationalLogService>();
builder.Services.AddSingleton<OperationalDiagnosticsService>();
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddSingleton<NewsPipeline>();
if (builder.Configuration.GetValue("Collector:EnableInWebHost", false))
{
    builder.Services.AddHostedService<ScheduledRefreshService>();
}

var appStartedAt = DateTimeOffset.UtcNow;
var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (context.Request.Path.StartsWithSegments("/admin"))
    {
        context.Response.Headers["X-Robots-Tag"] = "noindex,nofollow";
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (HttpContext context, AppPaths paths, IConfiguration configuration, AdminAuthService adminAuth) =>
{
    var feeds = await paths.ReadFeedUrlsAsync();
    var isAdmin = adminAuth.IsAuthenticated(context);
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
});

app.MapGet("/api/articles", async (HttpContext context, IArticleStore store, AdminAuthService adminAuth) =>
{
    if (!adminAuth.IsAuthenticated(context)) return AdminAuthService.AdminRequired();
    return Results.Ok(await store.ReadArticlesAsync());
});

app.MapGet("/api/groups", async (HttpContext context, IArticleStore store, AdminAuthService adminAuth) =>
{
    if (!adminAuth.IsAuthenticated(context)) return AdminAuthService.AdminRequired();
    return Results.Ok(await store.ReadGroupsAsync());
});

app.MapGet("/api/briefs", async (IArticleStore store, AppPaths paths) =>
{
    var articles = await store.ReadArticlesAsync();
    var feedUrls = await paths.ReadFeedUrlsAsync();
    var activeFeeds = feedUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var activePublishers = feedUrls
        .Select(feedUrl => RssSourceCatalog.SourceInfoForUrl(feedUrl).Publisher)
        .Where(publisher => !string.IsNullOrWhiteSpace(publisher))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (activeFeeds.Count > 0)
    {
        articles = articles
            .Where(article =>
                activeFeeds.Contains(article.FeedUrl) ||
                activePublishers.Contains(RssSourceCatalog.SourceInfoForUrl(article.FeedUrl).Publisher))
            .ToList();
    }

    var groups = await store.ReadGroupsAsync();
    return Results.Ok(ApiMapper.ToBriefs(groups, articles));
});

app.MapGet("/api/daily-summary", async (HttpContext context, string? date, bool? force, DailySummaryService dailySummaryService, AdminAuthService adminAuth, CancellationToken cancellationToken) =>
{
    try
    {
        if ((force.GetValueOrDefault() || !string.IsNullOrWhiteSpace(date)) && !adminAuth.IsAuthenticated(context))
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

        return Results.Ok(await dailySummaryService.GetOrCreateSummaryAsync(targetDate, force.GetValueOrDefault(), cancellationToken));
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
        Console.WriteLine($"[daily-summary] failed: {error}");
        return Results.Json(CreateLocalError(context, error), statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/weekly-summary", async (HttpContext context, string? endDate, bool? force, DailySummaryService dailySummaryService, AdminAuthService adminAuth, CancellationToken cancellationToken) =>
{
    try
    {
        if ((force.GetValueOrDefault() || !string.IsNullOrWhiteSpace(endDate)) && !adminAuth.IsAuthenticated(context))
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

        return Results.Ok(await dailySummaryService.GetOrCreateWeeklySummaryAsync(targetEndDate, force.GetValueOrDefault(), cancellationToken));
    }
    catch (Exception error) when (error is not OperationCanceledException)
    {
        Console.WriteLine($"[weekly-summary] failed: {error}");
        return Results.Json(CreateLocalError(context, error), statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/refresh", async (HttpContext context, NewsPipeline pipeline, OperationalLogService operationalLog, AdminAuthService adminAuth, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!adminAuth.IsAuthenticated(context)) return AdminAuthService.AdminRequired();
    if (!adminAuth.HasValidCsrf(context)) return AdminAuthService.CsrfRequired();
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
});

app.MapPost("/api/admin/fetch-missing-content", async (HttpContext context, int? limit, IArticleStore store, ArticleContentFetcher contentFetcher, AdminAuthService adminAuth, CancellationToken cancellationToken) =>
{
    if (!adminAuth.IsAuthenticated(context)) return AdminAuthService.AdminRequired();
    if (!adminAuth.HasValidCsrf(context)) return AdminAuthService.CsrfRequired();

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

app.MapPost("/api/admin/fetch-missing-images", async (HttpContext context, int? limit, IArticleStore store, ArticleContentFetcher contentFetcher, AdminAuthService adminAuth, CancellationToken cancellationToken) =>
{
    if (!adminAuth.IsAuthenticated(context)) return AdminAuthService.AdminRequired();
    if (!adminAuth.HasValidCsrf(context)) return AdminAuthService.CsrfRequired();

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

app.MapGet("/api/admin/diagnostics", async (HttpContext context, OperationalDiagnosticsService diagnostics, AdminAuthService adminAuth) =>
{
    if (!adminAuth.IsAuthenticated(context)) return AdminAuthService.AdminRequired();

    return Results.Ok(await diagnostics.BuildAsync(appStartedAt));
});

app.MapAdminEndpoints(appStartedAt);
app.MapFallbackToFile("index.html");

app.Run();

static object CreateLocalError(HttpContext context, Exception error)
{
    var isLoopback = context.Connection.RemoteIpAddress is { } remoteIpAddress && IPAddress.IsLoopback(remoteIpAddress);
    return isLoopback
        ? new { error = error.GetType().Name, message = error.Message }
        : new { error = "summary_failed" };
}
