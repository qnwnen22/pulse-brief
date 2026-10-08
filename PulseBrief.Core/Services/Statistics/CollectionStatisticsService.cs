namespace PulseBrief;

public sealed class CollectionStatisticsService(ICollectionStatisticsStore store)
{
    public const int MaxHistoryDays = 3660;

    public async Task<CollectionStatisticsResponse> ReadAsync(string period, DateOnly today, CancellationToken cancellationToken)
    {
        if (period is not ("7" or "30" or "all")) throw new ArgumentException("Supported periods: 7, 30, all.", nameof(period));
        var state = await store.ReadCollectionStatisticsStateAsync(cancellationToken);
        if (state is null) return new() { Period = period, TodayDate = KoreaDate.Key(today) };
        var firstDate = DateOnly.ParseExact(state.FirstDate, "yyyy-MM-dd");
        var from = period == "all" ? firstDate : today.AddDays(-int.Parse(period));
        if (from < firstDate) from = firstDate;
        var length = today.DayNumber - from.DayNumber;
        var previousFrom = from.AddDays(-length);
        var queryFrom = period != "all" && previousFrom >= firstDate ? previousFrom : from;
        var days = await store.ReadCollectionDaysAsync(queryFrom, today, cancellationToken);
        return Calculate(period, today, from, firstDate, days);
    }

    public static CollectionStatisticsResponse Calculate(string period, DateOnly today, DateOnly from, DateOnly firstDate, IReadOnlyList<CollectionDayStatistics> days)
    {
        var byDate = days.ToDictionary(day => day.Id, StringComparer.Ordinal);
        var trend = new List<CollectionTrendPoint>();
        var publishers = new Dictionary<string, long>(StringComparer.Ordinal);
        var categories = ArticleCategoryClassifier.Categories.ToDictionary(category => category, _ => 0L, StringComparer.Ordinal);
        var categoryCompletedDays = 0;
        var length = today.DayNumber - from.DayNumber;
        for (var date = from; date < today; date = date.AddDays(1))
        {
            byDate.TryGetValue(KoreaDate.Key(date), out var day);
            trend.Add(new(KoreaDate.Key(date), day?.IsComplete == true ? day.ArticleCount : null));
            if (day?.IsComplete != true) continue;
            foreach (var (name, count) in day.Publishers) publishers[name] = publishers.GetValueOrDefault(name) + count;
            if (!HasCurrentCategories(day)) continue;
            categoryCompletedDays++;
            foreach (var (name, count) in day.Categories) categories[name] += count;
        }
        var ready = length > 0 && trend.All(point => point.Count.HasValue);
        var total = ready ? trend.Sum(point => point.Count!.Value) : (long?)null;
        var categoriesReady = ready && categoryCompletedDays == length;
        long? previousTotal = null;
        if (period != "all" && length > 0 && from.AddDays(-length) >= firstDate)
        {
            var previous = Enumerable.Range(1, length).Select(offset => byDate.GetValueOrDefault(KoreaDate.Key(from.AddDays(-offset)))).ToList();
            if (previous.All(day => day?.IsComplete == true)) previousTotal = previous.Sum(day => day!.ArticleCount);
        }
        return new()
        {
            Period = period, TodayDate = KoreaDate.Key(today), FromDate = KoreaDate.Key(from), ToDate = KoreaDate.Key(today.AddDays(-1)),
            IsReady = ready, CompletedDays = trend.Count(point => point.Count.HasValue), ExpectedDays = length,
            Total = total, DailyAverage = ready ? Math.Round((double)total!.Value / length, 1) : null,
            TodayCount = byDate.GetValueOrDefault(KoreaDate.Key(today))?.ArticleCount,
            PreviousTotal = previousTotal,
            ChangePercent = ready && previousTotal > 0 ? Math.Round((total!.Value - previousTotal.Value) * 100d / previousTotal.Value, 1) : null,
            UpdatedAt = days.Count > 0 ? days.Max(day => day.UpdatedAt) : null,
            Trend = trend,
            Publishers = ready ? BuildShares(publishers, total!.Value, (name, count, share) => new CollectionPublisherShare(name, count, share)) : [],
            AreCategoriesReady = categoriesReady, CategoryCompletedDays = categoryCompletedDays,
            Categories = categoriesReady ? BuildShares(categories, total!.Value, (name, count, share) => new CollectionCategoryShare(name, count, share)) : []
        };
    }

    public static bool HasCurrentCategories(CollectionDayStatistics day) =>
        day.CategoryVersion == ArticleCategoryClassifier.StatisticsVersion
        && day.Categories.All(pair => pair.Value >= 0 && ArticleCategoryClassifier.Categories.Contains(pair.Key, StringComparer.Ordinal))
        && day.Categories.Values.Sum() == day.ArticleCount;

    private static List<T> BuildShares<T>(Dictionary<string, long> counts, long total, Func<string, long, double, T> create) =>
        counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => create(pair.Key, pair.Value, total > 0 ? Math.Round(pair.Value * 100d / total, 1) : 0)).ToList();
}
