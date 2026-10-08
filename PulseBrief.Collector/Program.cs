using PulseBrief;

var contentRoot = ResolveContentRoot();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(argument => !argument.Equals("--statistics-backfill", StringComparison.OrdinalIgnoreCase)).ToArray(),
    ContentRootPath = contentRoot
});

DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));
builder.Services.AddPulseBriefCore(builder.Configuration);
if (args.Contains("--statistics-backfill", StringComparer.OrdinalIgnoreCase))
{
    using var host = builder.Build();
    await host.Services.GetRequiredService<CollectionStatisticsMaintenance>().RefreshAsync(true, CancellationToken.None);
    Console.WriteLine("[statistics] backfill completed");
    return;
}
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
