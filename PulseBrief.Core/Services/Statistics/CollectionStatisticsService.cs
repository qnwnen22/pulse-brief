namespace PulseBrief;

public sealed class CollectionStatisticsService(ICollectionStatisticsStore store)
{
    public const int MaxHistoryDays = 3660;
    public const int PublisherCategoryVersion = 1;

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
        var publisherCategories = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        var publisherCategoryCompletedDays = 0;
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
            if (!HasCurrentPublisherCategories(day)) continue;
            publisherCategoryCompletedDays++;
            foreach (var (publisher, counts) in day.PublisherCategories)
            {
                if (!publisherCategories.TryGetValue(publisher, out var breakdown))
                    publisherCategories[publisher] = breakdown = new(StringComparer.Ordinal);
                foreach (var (category, count) in counts) breakdown[category] = breakdown.GetValueOrDefault(category) + count;
            }
        }
        var ready = length > 0 && trend.All(point => point.Count.HasValue);
        var total = ready ? trend.Sum(point => point.Count!.Value) : (long?)null;
        var categoriesReady = ready && categoryCompletedDays == length;
        var publisherCategoriesReady = ready && publisherCategoryCompletedDays == length;
        long? previousTotal = null;
        List<CollectionDayStatistics?> previous = [];
        if (period != "all" && length > 0 && from.AddDays(-length) >= firstDate)
        {
            previous = Enumerable.Range(1, length).Select(offset => byDate.GetValueOrDefault(KoreaDate.Key(from.AddDays(-offset)))).ToList();
            if (previous.All(day => day?.IsComplete == true)) previousTotal = previous.Sum(day => day!.ArticleCount);
        }
        var comparisonReady = categoriesReady && previousTotal.HasValue && previous.All(day => HasCurrentCategories(day!));
        var categoryShares = categoriesReady ? BuildShares(categories, total!.Value, (name, count, share) => new CollectionCategoryShare(name, count, share)) : [];
        if (comparisonReady)
        {
            categoryShares = categoryShares.Select(item =>
            {
                var previousCount = previous.Sum(day => day!.Categories.GetValueOrDefault(item.Category));
                return item with
                {
                    PreviousCount = previousCount,
                    ChangePercent = previousCount > 0 ? Math.Round((item.Count - previousCount) * 100d / previousCount, 1) : null,
                    ShareChangePoints = total > 0 && previousTotal > 0
                        ? Math.Round(item.Count * 100d / total.Value - previousCount * 100d / previousTotal.Value, 1) : null
                };
            }).ToList();
        }
        return new()
        {
            Period = period, TodayDate = KoreaDate.Key(today), FromDate = KoreaDate.Key(from), ToDate = KoreaDate.Key(today.AddDays(-1)),
            IsReady = ready, CompletedDays = trend.Count(point => point.Count.HasValue), ExpectedDays = length,
            Total = total, DailyAverage = ready ? Math.Round((double)total!.Value / length, 1) : null,
            TodayCount = byDate.GetValueOrDefault(KoreaDate.Key(today))?.ArticleCount,
            PreviousTotal = previousTotal,
            PreviousFromDate = period != "all" && length > 0 ? KoreaDate.Key(from.AddDays(-length)) : null,
            PreviousToDate = period != "all" && length > 0 ? KoreaDate.Key(from.AddDays(-1)) : null,
            ChangePercent = ready && previousTotal > 0 ? Math.Round((total!.Value - previousTotal.Value) * 100d / previousTotal.Value, 1) : null,
            UpdatedAt = days.Count > 0 ? days.Max(day => day.UpdatedAt) : null,
            Trend = trend,
            Publishers = ready ? BuildShares(publishers, total!.Value, (name, count, share) => new CollectionPublisherShare(name, count, share)) : [],
            PublisherTrends = BuildPublisherTrends(period, trend, byDate, publishers),
            Weekdays = BuildWeekdays(trend), ArePublisherCategoriesReady = publisherCategoriesReady,
            PublisherCategoryCompletedDays = publisherCategoryCompletedDays,
            PublisherCategories = publisherCategoriesReady ? BuildPublisherCategoryShares(publishers, publisherCategories) : [],
            AreCategoriesReady = categoriesReady, CategoryCompletedDays = categoryCompletedDays,
            IsCategoryComparisonReady = comparisonReady, Categories = categoryShares
        };
    }

    public static bool HasCurrentCategories(CollectionDayStatistics day) =>
        day.CategoryVersion == ArticleCategoryClassifier.StatisticsVersion
        && day.Categories.All(pair => pair.Value >= 0 && ArticleCategoryClassifier.Categories.Contains(pair.Key, StringComparer.Ordinal))
        && day.Categories.Values.Sum() == day.ArticleCount;

    public static bool HasCurrentPublisherCategories(CollectionDayStatistics day) =>
        day.PublisherCategoryVersion == PublisherCategoryVersion && HasCurrentCategories(day)
        && day.Publishers.All(pair => pair.Value >= 0) && day.Publishers.Values.Sum() == day.ArticleCount
        && day.PublisherCategories.All(pair => day.Publishers.TryGetValue(pair.Key, out var total)
            && pair.Value.All(cell => cell.Value >= 0 && ArticleCategoryClassifier.Categories.Contains(cell.Key, StringComparer.Ordinal))
            && pair.Value.Values.Sum() == total)
        && day.Publishers.All(pair => (day.PublisherCategories.GetValueOrDefault(pair.Key)?.Values.Sum() ?? 0) == pair.Value)
        && ArticleCategoryClassifier.Categories.All(category => day.PublisherCategories.Values.Sum(counts => counts.GetValueOrDefault(category))
            == day.Categories.GetValueOrDefault(category));

    private static List<CollectionWeekdayStatistics> BuildWeekdays(List<CollectionTrendPoint> trend)
    {
        string[] labels = ["월요일", "화요일", "수요일", "목요일", "금요일", "토요일", "일요일"];
        var groups = trend.GroupBy(point => ((int)DateOnly.ParseExact(point.Date, "yyyy-MM-dd").DayOfWeek + 6) % 7)
            .ToDictionary(group => group.Key, group => group.ToList());
        return Enumerable.Range(0, 7).Select(day =>
        {
            var points = groups.GetValueOrDefault(day) ?? [];
            var samples = points.Where(point => point.Count.HasValue).ToList();
            var total = samples.Count > 0 ? samples.Sum(point => point.Count!.Value) : (long?)null;
            return new CollectionWeekdayStatistics(day, labels[day], samples.Count, points.Count, total,
                samples.Count > 0 ? Math.Round(total!.Value / (double)samples.Count, 1) : null);
        }).ToList();
    }

    private static List<T> BuildShares<T>(Dictionary<string, long> counts, long total, Func<string, long, double, T> create) =>
        counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => create(pair.Key, pair.Value, total > 0 ? Math.Round(pair.Value * 100d / total, 1) : 0)).ToList();

    private static List<CollectionPublisherCategories> BuildPublisherCategoryShares(Dictionary<string, long> publishers,
        Dictionary<string, Dictionary<string, long>> publisherCategories) =>
        publishers.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CollectionPublisherCategories(pair.Key, pair.Value, BuildShares(
                ArticleCategoryClassifier.Categories.ToDictionary(category => category,
                    category => publisherCategories.GetValueOrDefault(pair.Key)?.GetValueOrDefault(category) ?? 0L, StringComparer.Ordinal),
                pair.Value, (name, count, share) => new CollectionCategoryShare(name, count, share)))).ToList();

    private static List<CollectionPublisherTrend> BuildPublisherTrends(string period, List<CollectionTrendPoint> trend,
        Dictionary<string, CollectionDayStatistics> byDate, Dictionary<string, long> publishers) =>
        publishers.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
        {
            var points = trend.Select(point => new CollectionTrendPoint(point.Date,
                point.Count.HasValue ? byDate[point.Date].Publishers.GetValueOrDefault(pair.Key) : null)).ToList();
            if (period == "all")
                points = points.GroupBy(point => point.Date[..7]).Select(month => new CollectionTrendPoint(month.Key,
                    month.All(point => point.Count.HasValue) ? month.Sum(point => point.Count!.Value) : null)).ToList();
            return new CollectionPublisherTrend(pair.Key, points);
        }).ToList();
}
