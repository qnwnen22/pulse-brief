namespace PulseBrief;

public interface ICollectionStatisticsStore
{
    Task<CollectionStatisticsState?> ReadCollectionStatisticsStateAsync(CancellationToken cancellationToken);
    Task<List<CollectionDayStatistics>> ReadCollectionDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task InitializeCollectionStatisticsAsync(CancellationToken cancellationToken);
    Task RefreshCollectionDayAsync(DateOnly date, DateOnly today, CancellationToken cancellationToken);
}
