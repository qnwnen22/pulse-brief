using PulseBrief;

var contentRoot = ResolveContentRoot();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRoot
});

DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));
builder.Services.AddPulseBriefCore(builder.Configuration);
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
