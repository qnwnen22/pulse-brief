using PulseBrief;

namespace PulseBrief.Tests;

public sealed class FakeCollectionStatisticsStore : ICollectionStatisticsStore
{
    public CollectionStatisticsState? State { get; set; } = new() { FirstDate = KoreaDate.Key(KoreaDate.Today().AddDays(-60)) };
    public List<CollectionDayStatistics> Days { get; } = Enumerable.Range(0, 61).Select(offset => new CollectionDayStatistics
    {
        Id = KoreaDate.Key(KoreaDate.Today().AddDays(-offset)), IsComplete = offset > 0,
        ArticleCount = offset == 0 ? 5 : 10,
        Publishers = new() { ["연합뉴스"] = offset == 0 ? 3 : 6, ["한겨레"] = offset == 0 ? 2 : 4 },
        Categories = new() { ["정치/정책"] = offset == 0 ? 3 : 6, ["사회"] = offset == 0 ? 2 : 4 },
        CategoryVersion = ArticleCategoryClassifier.StatisticsVersion
    }).ToList();
    public List<DateOnly> RefreshedDates { get; } = [];
    public bool FailReads { get; set; }
    public int IndexInitializations { get; private set; }

    public Task<CollectionStatisticsState?> ReadCollectionStatisticsStateAsync(CancellationToken cancellationToken) =>
        FailReads ? throw new InvalidOperationException("Fixture cache unavailable") : Task.FromResult(State);

    public Task<List<CollectionDayStatistics>> ReadCollectionDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult(Days.Where(day => string.CompareOrdinal(day.Id, KoreaDate.Key(from)) >= 0 && string.CompareOrdinal(day.Id, KoreaDate.Key(to)) <= 0).ToList());

    public Task InitializeCollectionStatisticsAsync(CancellationToken cancellationToken)
    {
        IndexInitializations++;
        return Task.CompletedTask;
    }

    public Task RefreshCollectionDayAsync(DateOnly date, DateOnly today, CancellationToken cancellationToken)
    {
        RefreshedDates.Add(date);
        Days.RemoveAll(day => day.Id == KoreaDate.Key(date));
        Days.Add(new()
        {
            Id = KoreaDate.Key(date), IsComplete = date < today, ArticleCount = 10, Publishers = new() { ["연합뉴스"] = 10 },
            Categories = new() { ["사회"] = 10 }, CategoryVersion = ArticleCategoryClassifier.StatisticsVersion
        });
        return Task.CompletedTask;
    }
}
