namespace PulseBrief;

/// <summary>설정된 주기마다 뉴스 수집 파이프라인을 자동 실행하는 백그라운드 서비스입니다.</summary>
public sealed class ScheduledRefreshService(NewsPipeline pipeline, IConfiguration configuration, OperationalLogService operationalLog) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(configuration.GetValue("AutoRefreshMinutes", 10));

    /// <summary>애플리케이션 실행 중 주기적으로 파이프라인을 실행하고 실패를 로그로 남깁니다.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Console.WriteLine("[refresh] scheduled pipeline started");
                await operationalLog.RecordAsync("info", "scheduled_refresh_started", "Scheduled refresh started.", new
                {
                    intervalMinutes = _interval.TotalMinutes
                }, stoppingToken);
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
                await operationalLog.RecordAsync("error", "scheduled_refresh_failed", "Scheduled refresh failed.", new
                {
                    errorType = error.GetType().Name,
                    error.Message
                }, CancellationToken.None);
            }
        }
    }
}
