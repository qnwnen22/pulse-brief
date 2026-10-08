namespace PulseBrief;

public sealed class CollectionStatisticsMaintenance(ICollectionStatisticsStore store)
{
    public async Task RefreshAsync(bool backfill, CancellationToken cancellationToken)
    {
        if (backfill) await store.InitializeCollectionStatisticsAsync(cancellationToken);
        var state = await store.ReadCollectionStatisticsStateAsync(cancellationToken);
        if (state is null) return;
        var today = KoreaDate.Today();
        var first = DateOnly.ParseExact(state.FirstDate, "yyyy-MM-dd");
        await store.RefreshCollectionDayAsync(today, today, cancellationToken);
        if (first < today) await store.RefreshCollectionDayAsync(today.AddDays(-1), today, cancellationToken);
        var saved = await store.ReadCollectionDaysAsync(first, today, cancellationToken);
        var complete = saved.Where(day => day.IsComplete).Select(day => day.Id).ToHashSet(StringComparer.Ordinal);
        var remaining = backfill ? CollectionStatisticsService.MaxHistoryDays : 1;
        for (var date = today.AddDays(-2); date >= first && remaining > 0; date = date.AddDays(-1))
        {
            if (complete.Contains(KoreaDate.Key(date))) continue;
            await store.RefreshCollectionDayAsync(date, today, cancellationToken);
            Console.WriteLine($"[statistics] cached {KoreaDate.Key(date)}");
            remaining--;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
