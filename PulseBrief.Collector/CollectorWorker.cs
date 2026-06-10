using PulseBrief;

/// <summary>웹 서버와 분리되어 RSS 수집 파이프라인을 주기적으로 실행하는 백그라운드 작업자입니다.</summary>
public sealed class CollectorWorker(
    NewsPipeline pipeline,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    OperationalLogService operationalLog) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("AutoRefreshMinutes", 10)));
    private readonly bool _runOnce = Environment.GetCommandLineArgs().Any(argument => argument.Equals("--once", StringComparison.OrdinalIgnoreCase))
        || configuration.GetValue("Collector:RunOnce", false);
    private readonly bool _runOnStartup = configuration.GetValue("Collector:RunOnStartup", true);

    /// <summary>설정에 따라 1회 실행 또는 주기 실행 모드로 뉴스 수집 파이프라인을 구동합니다.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await operationalLog.RecordAsync("info", "collector_started", "PulseBrief collector started.", new
        {
            runOnce = _runOnce,
            runOnStartup = _runOnStartup,
            intervalMinutes = _interval.TotalMinutes
        }, stoppingToken);

        if (_runOnce)
        {
            await RunPipelineAsync(stoppingToken);
            lifetime.StopApplication();
            return;
        }

        if (_runOnStartup)
        {
            await RunPipelineAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPipelineAsync(stoppingToken);
        }
    }

    private async Task RunPipelineAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine("[collector] pipeline started");
            var result = await pipeline.RunAsync(cancellationToken);
            Console.WriteLine($"[collector] pipeline finished: fetched={result.FetchedCount}, articles={result.ArticleCount}, groups={result.GroupCount}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            Console.WriteLine($"[collector] pipeline failed: {error}");
            await operationalLog.RecordAsync("error", "collector_pipeline_failed", "Collector pipeline failed.", new
            {
                errorType = error.GetType().Name,
                error.Message
            }, CancellationToken.None);
        }
    }
}
