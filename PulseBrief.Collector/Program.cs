using PulseBrief;

var contentRoot = ResolveContentRoot();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRoot
});

DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));
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
builder.Services.AddSingleton<NewsPipeline>();
builder.Services.AddHostedService<CollectorWorker>();

await builder.Build().RunAsync();

static string ResolveContentRoot()
{
    var candidates = new[]
    {
        Directory.GetCurrentDirectory(),
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..")),
        AppContext.BaseDirectory,
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
    };

    return candidates.FirstOrDefault(path => Directory.Exists(Path.Combine(path, "config")))
        ?? Directory.GetCurrentDirectory();
}
