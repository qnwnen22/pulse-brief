namespace PulseBrief;

public sealed class ScheduledRefreshService(NewsPipeline pipeline, IConfiguration configuration) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(configuration.GetValue("AutoRefreshMinutes", 10));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Console.WriteLine("[refresh] scheduled pipeline started");
                var result = await pipeline.RunAsync(stoppingToken);
                Console.WriteLine($"[refresh] scheduled pipeline finished: fetched={result.FetchedCount}, articles={result.ArticleCount}, groups={result.GroupCount}");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                Console.WriteLine($"[refresh] scheduled pipeline failed: {error}");
            }
        }
    }
}
