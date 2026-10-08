using PulseBrief;

namespace PulseBrief.Tests;

public static class CollectionStatisticsTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var today = KoreaDate.Today();
        var first = today.AddDays(-14);
        var days = Enumerable.Range(0, 15).Select(offset => new CollectionDayStatistics
        {
            Id = KoreaDate.Key(today.AddDays(-offset)), IsComplete = offset > 0,
            ArticleCount = offset == 0 ? 1000 : offset <= 7 ? 20 : 10,
            Publishers = new() { ["연합뉴스"] = offset <= 7 ? 12 : 6, ["한겨레"] = offset <= 7 ? 8 : 4 }
        }).ToList();
        var result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.IsReady && result.Total == 140 && result.DailyAverage == 20, "Today's unfinished day leaked into averages.");
        check(result.TodayCount == 1000 && result.PreviousTotal == 70 && result.ChangePercent == 100, "Equal-length period comparison failed.");
        check(result.Publishers[0] is { Publisher: "연합뉴스", Count: 84, Share: 60 }, "Publisher counts or shares are incorrect.");
        check(result.Publishers.Sum(item => item.Count) == result.Total, "Publisher count sum differs from total.");

        var zero = days.Single(day => day.Id == KoreaDate.Key(today.AddDays(-1)));
        zero.ArticleCount = 0;
        zero.Publishers.Clear();
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.IsReady && result.Total == 120 && result.DailyAverage == 17.1 && result.ExpectedDays == 7, "Zero-article dates must be included in the average denominator.");
        zero.IsComplete = false;
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(!result.IsReady && result.Total is null && result.DailyAverage is null && result.Publishers.Count == 0, "Incomplete cache was presented as a complete period.");
        check(result.Trend[^1].Count is null && result.CompletedDays == 6, "Missing dates must not be treated as zero.");
        zero.IsComplete = true;
        foreach (var day in days.Where(day => string.CompareOrdinal(day.Id, KoreaDate.Key(today.AddDays(-7))) < 0)) day.ArticleCount = 0;
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.PreviousTotal == 0 && result.ChangePercent is null, "Zero comparison denominator must not produce infinity.");
        result = CollectionStatisticsService.Calculate("all", today, first, first, days);
        check(result.PreviousTotal is null && result.ChangePercent is null, "All-history periods must not have a fabricated comparison.");
        check(KoreaDate.StartOfDay(new DateOnly(2026, 10, 8)).UtcDateTime == new DateTime(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc), "KST midnight boundary changed.");
        check(RssSourceCatalog.SourceInfoForUrl("https://www.mk.co.kr/rss/30000001/").Publisher ==
            RssSourceCatalog.SourceInfoForUrl("https://www.mk.co.kr/rss/30200030/").Publisher, "RSS channels did not normalize to one publisher.");

        var store = new FakeCollectionStatisticsStore();
        var service = new CollectionStatisticsService(store);
        var cached = await service.ReadAsync("7", today, CancellationToken.None);
        check(cached.Total == 70 && store.IndexInitializations == 0 && store.RefreshedDates.Count == 0, "Public statistics read attempted article maintenance or index provisioning.");
        store.State = null;
        check(!(await service.ReadAsync("7", today, CancellationToken.None)).IsReady, "Absent cache did not return a waiting state.");
        store.State = new() { FirstDate = KoreaDate.Key(today.AddDays(-60)) };
        var missing = KoreaDate.Key(today.AddDays(-4));
        store.Days.RemoveAll(day => day.Id == missing || day.Id == KoreaDate.Key(today.AddDays(-5)));
        var maintenance = new CollectionStatisticsMaintenance(store);
        await maintenance.RefreshAsync(false, CancellationToken.None);
        check(store.RefreshedDates.Count == 3 && store.RefreshedDates[^1] == today.AddDays(-4), "Routine maintenance must repair at most one historical day per cycle.");
        check(store.IndexInitializations == 0, "Routine maintenance must not build indexes.");
        await maintenance.RefreshAsync(true, CancellationToken.None);
        check(store.IndexInitializations == 1 && store.Days.Any(day => day.Id == KoreaDate.Key(today.AddDays(-5)) && day.IsComplete), "Explicit backfill did not repair missing history.");
        check(store.Days.Select(day => day.Id).Distinct().Count() == store.Days.Count, "Repeated maintenance created duplicate dates.");
    }
}
