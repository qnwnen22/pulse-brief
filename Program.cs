using System.Text.Json;
using System.Text.Json.Serialization;
using PulseBrief;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddHttpClient<RssCollector>();
builder.Services.AddSingleton<AppPaths>();
builder.Services.AddSingleton<ArticleStore>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<ArticleClusterer>();
builder.Services.AddSingleton<BriefGenerator>();
builder.Services.AddSingleton<NewsPipeline>();
builder.Services.AddHostedService<ScheduledRefreshService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (AppPaths paths) =>
{
    var feeds = await paths.ReadFeedUrlsAsync();
    return Results.Ok(new
    {
        ok = true,
        server = ".NET",
        hasOpenAiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
        rssFeedCount = feeds.Count
    });
});

app.MapGet("/api/articles", async (ArticleStore store) => Results.Ok(await store.ReadArticlesAsync()));

app.MapGet("/api/groups", async (ArticleStore store) => Results.Ok(await store.ReadGroupsAsync()));

app.MapGet("/api/briefs", async (ArticleStore store) =>
{
    var articles = await store.ReadArticlesAsync();
    var groups = await store.ReadGroupsAsync();
    return Results.Ok(ApiMapper.ToBriefs(groups, articles));
});

app.MapPost("/api/refresh", async (NewsPipeline pipeline, CancellationToken cancellationToken) =>
{
    var result = await pipeline.RunAsync(cancellationToken);
    return Results.Ok(result);
});

app.MapFallbackToFile("index.html");

app.Run();
