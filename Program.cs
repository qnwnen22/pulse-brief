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
builder.Services.AddHttpClient<ArticleContentFetcher>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<ArticleClusterer>();
builder.Services.AddSingleton<BriefGenerator>();
builder.Services.AddHttpClient<OpenAiDailySummaryClient>();
builder.Services.AddSingleton<DailySummaryService>();
builder.Services.AddSingleton<NewsPipeline>();
builder.Services.AddHostedService<ScheduledRefreshService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (AppPaths paths, IConfiguration configuration) =>
{
    var feeds = await paths.ReadFeedUrlsAsync();
    return Results.Ok(new
    {
        ok = true,
        server = ".NET",
        database = configuration["Storage:Provider"] ?? "MongoDB",
        hasOpenAiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
        rssFeedCount = feeds.Count
    });
});

app.MapGet("/api/articles", async (IArticleStore store) => Results.Ok(await store.ReadArticlesAsync()));

app.MapGet("/api/groups", async (IArticleStore store) => Results.Ok(await store.ReadGroupsAsync()));

app.MapGet("/api/briefs", async (IArticleStore store) =>
{
    var articles = await store.ReadArticlesAsync();
    var groups = await store.ReadGroupsAsync();
    return Results.Ok(ApiMapper.ToBriefs(groups, articles));
});

app.MapGet("/api/daily-summary", async (string? date, bool? force, DailySummaryService dailySummaryService, CancellationToken cancellationToken) =>
{
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
});

app.MapGet("/api/weekly-summary", async (string? endDate, bool? force, DailySummaryService dailySummaryService, CancellationToken cancellationToken) =>
{
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
});

app.MapPost("/api/refresh", async (NewsPipeline pipeline, CancellationToken cancellationToken) =>
{
    var result = await pipeline.RunAsync(cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/api/admin/fetch-missing-content", async (HttpContext context, int? limit, IArticleStore store, ArticleContentFetcher contentFetcher, CancellationToken cancellationToken) =>
{
    if (context.Connection.RemoteIpAddress is not { } remoteIpAddress || !IPAddress.IsLoopback(remoteIpAddress))
    {
        return Results.Forbid();
    }

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

app.MapPost("/api/admin/fetch-missing-images", async (HttpContext context, int? limit, IArticleStore store, ArticleContentFetcher contentFetcher, CancellationToken cancellationToken) =>
{
    if (context.Connection.RemoteIpAddress is not { } remoteIpAddress || !IPAddress.IsLoopback(remoteIpAddress))
    {
        return Results.Forbid();
    }

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
