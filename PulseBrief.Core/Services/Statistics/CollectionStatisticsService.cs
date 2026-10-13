namespace PulseBrief;

public sealed class CollectionStatisticsService(ICollectionStatisticsStore store)
{
    public const int MaxHistoryDays = 3660;
    public const int PublisherCategoryVersion = 1;

    public async Task<CollectionStatisticsResponse> ReadAsync(
        string period,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (period is not ("7" or "30" or "all"))
        {
            throw new ArgumentException("Supported periods: 7, 30, all.", nameof(period));
        }

        var state = await store.ReadCollectionStatisticsStateAsync(cancellationToken);
        if (state is null)
        {
            return new() { Period = period, TodayDate = KoreaDate.Key(today) };
        }

        var firstDate = DateOnly.ParseExact(state.FirstDate, "yyyy-MM-dd");
        var from = period == "all" ? firstDate : today.AddDays(-int.Parse(period));
        if (from < firstDate)
        {
            from = firstDate;
        }

        var expectedDays = today.DayNumber - from.DayNumber;
        var previousFrom = from.AddDays(-expectedDays);
        var queryFrom = period != "all" && previousFrom >= firstDate ? previousFrom : from;

        // Current and comparison periods share one bounded cache read; no articles are queried here.
        var days = await store.ReadCollectionDaysAsync(queryFrom, today, cancellationToken);
        return Calculate(period, today, from, firstDate, days);
    }

    public static CollectionStatisticsResponse Calculate(
        string period,
        DateOnly today,
        DateOnly from,
        DateOnly firstDate,
        IReadOnlyList<CollectionDayStatistics> days)
    {
        var daysByDate = days.ToDictionary(day => day.Id, StringComparer.Ordinal);
        var trend = new List<CollectionTrendPoint>();
        var publisherCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var categoryCounts = ArticleCategoryClassifier.Categories.ToDictionary(
            category => category,
            _ => 0L,
            StringComparer.Ordinal);
        var categoryCompletedDays = 0;
        var publisherCategoryCounts = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        var publisherCategoryCompletedDays = 0;
        var expectedDays = today.DayNumber - from.DayNumber;

        for (var date = from; date < today; date = date.AddDays(1))
        {
            var dateKey = KoreaDate.Key(date);
            daysByDate.TryGetValue(dateKey, out var day);
            trend.Add(new(dateKey, day?.IsComplete == true ? day.ArticleCount : null));
            if (day?.IsComplete != true)
            {
                continue;
            }

            foreach (var (publisher, count) in day.Publishers)
            {
                publisherCounts[publisher] = publisherCounts.GetValueOrDefault(publisher) + count;
            }

            if (!HasCurrentCategories(day))
            {
                continue;
            }

            categoryCompletedDays++;
            foreach (var (category, count) in day.Categories)
            {
                categoryCounts[category] += count;
            }

            if (!HasCurrentPublisherCategories(day))
            {
                continue;
            }

            publisherCategoryCompletedDays++;
            foreach (var (publisher, counts) in day.PublisherCategories)
            {
                if (!publisherCategoryCounts.TryGetValue(publisher, out var breakdown))
                {
                    breakdown = new(StringComparer.Ordinal);
                    publisherCategoryCounts[publisher] = breakdown;
                }

                foreach (var (category, count) in counts)
                {
                    breakdown[category] = breakdown.GetValueOrDefault(category) + count;
                }
            }
        }

        // Missing dates are unknown, not zero. Each breakdown has its own cache readiness.
        var isReady = expectedDays > 0 && trend.All(point => point.Count.HasValue);
        var total = isReady ? trend.Sum(point => point.Count!.Value) : (long?)null;
        var areCategoriesReady = isReady && categoryCompletedDays == expectedDays;
        var arePublisherCategoriesReady = isReady && publisherCategoryCompletedDays == expectedDays;
        var previousDays = GetPreviousPeriodDays(period, from, firstDate, expectedDays, daysByDate);
        long? previousTotal = null;
        if (previousDays.Count > 0 && previousDays.All(day => day?.IsComplete == true))
        {
            previousTotal = previousDays.Sum(day => day!.ArticleCount);
        }

        var isCategoryComparisonReady = areCategoriesReady
            && previousTotal.HasValue
            && previousDays.All(day => HasCurrentCategories(day!));
        var categoryShares = areCategoriesReady
            ? BuildShares(categoryCounts, total!.Value, (name, count, share) => new CollectionCategoryShare(name, count, share))
            : [];
        if (isCategoryComparisonReady)
        {
            categoryShares = AddCategoryComparison(categoryShares, previousDays, total!.Value, previousTotal!.Value);
        }

        return new()
        {
            Period = period,
            TodayDate = KoreaDate.Key(today),
            FromDate = KoreaDate.Key(from),
            ToDate = KoreaDate.Key(today.AddDays(-1)),
            IsReady = isReady,
            CompletedDays = trend.Count(point => point.Count.HasValue),
            ExpectedDays = expectedDays,
            Total = total,
            DailyAverage = isReady ? Math.Round((double)total!.Value / expectedDays, 1) : null,
            TodayCount = daysByDate.GetValueOrDefault(KoreaDate.Key(today))?.ArticleCount,
            PreviousTotal = previousTotal,
            PreviousFromDate = period != "all" && expectedDays > 0 ? KoreaDate.Key(from.AddDays(-expectedDays)) : null,
            PreviousToDate = period != "all" && expectedDays > 0 ? KoreaDate.Key(from.AddDays(-1)) : null,
            ChangePercent = isReady && previousTotal > 0
                ? Math.Round((total!.Value - previousTotal.Value) * 100d / previousTotal.Value, 1)
                : null,
            UpdatedAt = days.Count > 0 ? days.Max(day => day.UpdatedAt) : null,
            Trend = trend,
            Publishers = isReady
                ? BuildShares(publisherCounts, total!.Value, (name, count, share) => new CollectionPublisherShare(name, count, share))
                : [],
            PublisherTrends = BuildPublisherTrends(period, trend, daysByDate, publisherCounts),
            Weekdays = BuildWeekdays(trend),
            ArePublisherCategoriesReady = arePublisherCategoriesReady,
            PublisherCategoryCompletedDays = publisherCategoryCompletedDays,
            PublisherCategories = arePublisherCategoriesReady
                ? BuildPublisherCategoryShares(publisherCounts, publisherCategoryCounts)
                : [],
            AreCategoriesReady = areCategoriesReady,
            CategoryCompletedDays = categoryCompletedDays,
            IsCategoryComparisonReady = isCategoryComparisonReady,
            Categories = categoryShares
        };
    }

    public static bool HasCurrentCategories(CollectionDayStatistics day)
    {
        return day.CategoryVersion == ArticleCategoryClassifier.StatisticsVersion
            && HasValidCategoryCounts(day.Categories)
            && day.Categories.Values.Sum() == day.ArticleCount;
    }

    public static bool HasCurrentPublisherCategories(CollectionDayStatistics day)
    {
        if (day.PublisherCategoryVersion != PublisherCategoryVersion || !HasCurrentCategories(day))
        {
            return false;
        }

        if (day.Publishers.Any(publisher => publisher.Value < 0)
            || day.Publishers.Values.Sum() != day.ArticleCount)
        {
            return false;
        }

        foreach (var (publisher, categoryCounts) in day.PublisherCategories)
        {
            if (!day.Publishers.TryGetValue(publisher, out var publisherTotal)
                || !HasValidCategoryCounts(categoryCounts)
                || categoryCounts.Values.Sum() != publisherTotal)
            {
                return false;
            }
        }

        foreach (var (publisher, count) in day.Publishers)
        {
            var rowTotal = day.PublisherCategories.GetValueOrDefault(publisher)?.Values.Sum() ?? 0;
            if (rowTotal != count)
            {
                return false;
            }
        }

        foreach (var category in ArticleCategoryClassifier.Categories)
        {
            var columnTotal = day.PublisherCategories.Values.Sum(counts => counts.GetValueOrDefault(category));
            if (columnTotal != day.Categories.GetValueOrDefault(category))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasValidCategoryCounts(Dictionary<string, long> counts)
    {
        return counts.All(pair => pair.Value >= 0
            && ArticleCategoryClassifier.Categories.Contains(pair.Key, StringComparer.Ordinal));
    }

    private static List<CollectionDayStatistics?> GetPreviousPeriodDays(
        string period,
        DateOnly from,
        DateOnly firstDate,
        int expectedDays,
        Dictionary<string, CollectionDayStatistics> daysByDate)
    {
        if (period == "all" || expectedDays <= 0 || from.AddDays(-expectedDays) < firstDate)
        {
            return [];
        }

        return Enumerable.Range(1, expectedDays)
            .Select(offset => daysByDate.GetValueOrDefault(KoreaDate.Key(from.AddDays(-offset))))
            .ToList();
    }

    private static List<CollectionCategoryShare> AddCategoryComparison(
        List<CollectionCategoryShare> categoryShares,
        List<CollectionDayStatistics?> previousDays,
        long total,
        long previousTotal)
    {
        return categoryShares.Select(category =>
        {
            var previousCount = previousDays.Sum(day => day!.Categories.GetValueOrDefault(category.Category));
            return category with
            {
                PreviousCount = previousCount,
                ChangePercent = previousCount > 0
                    ? Math.Round((category.Count - previousCount) * 100d / previousCount, 1)
                    : null,
                ShareChangePoints = total > 0 && previousTotal > 0
                    ? Math.Round(category.Count * 100d / total - previousCount * 100d / previousTotal, 1)
                    : null
            };
        }).ToList();
    }

    private static List<CollectionWeekdayStatistics> BuildWeekdays(List<CollectionTrendPoint> trend)
    {
        string[] labels = ["월요일", "화요일", "수요일", "목요일", "금요일", "토요일", "일요일"];
        var groups = trend
            .GroupBy(point => ((int)DateOnly.ParseExact(point.Date, "yyyy-MM-dd").DayOfWeek + 6) % 7)
            .ToDictionary(group => group.Key, group => group.ToList());
        return Enumerable.Range(0, 7).Select(weekday =>
        {
            var points = groups.GetValueOrDefault(weekday) ?? [];
            var samples = points.Where(point => point.Count.HasValue).ToList();
            var total = samples.Count > 0 ? samples.Sum(point => point.Count!.Value) : (long?)null;
            return new CollectionWeekdayStatistics(
                weekday,
                labels[weekday],
                samples.Count,
                points.Count,
                total,
                samples.Count > 0 ? Math.Round(total!.Value / (double)samples.Count, 1) : null);
        }).ToList();
    }

    private static List<T> BuildShares<T>(
        Dictionary<string, long> counts,
        long total,
        Func<string, long, double, T> create)
    {
        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => create(pair.Key, pair.Value, total > 0 ? Math.Round(pair.Value * 100d / total, 1) : 0))
            .ToList();
    }

    private static List<CollectionPublisherCategories> BuildPublisherCategoryShares(
        Dictionary<string, long> publishers,
        Dictionary<string, Dictionary<string, long>> publisherCategories)
    {
        return publishers
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(publisher =>
            {
                var categoryCounts = ArticleCategoryClassifier.Categories.ToDictionary(
                    category => category,
                    category => publisherCategories.GetValueOrDefault(publisher.Key)?.GetValueOrDefault(category) ?? 0L,
                    StringComparer.Ordinal);
                var shares = BuildShares(
                    categoryCounts,
                    publisher.Value,
                    (name, count, share) => new CollectionCategoryShare(name, count, share));

                return new CollectionPublisherCategories(publisher.Key, publisher.Value, shares);
            })
            .ToList();
    }

    private static List<CollectionPublisherTrend> BuildPublisherTrends(
        string period,
        List<CollectionTrendPoint> trend,
        Dictionary<string, CollectionDayStatistics> daysByDate,
        Dictionary<string, long> publishers)
    {
        return publishers
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(publisher =>
            {
                var points = trend.Select(point => new CollectionTrendPoint(
                    point.Date,
                    point.Count.HasValue ? daysByDate[point.Date].Publishers.GetValueOrDefault(publisher.Key) : null))
                    .ToList();

                if (period == "all")
                {
                    points = points.GroupBy(point => point.Date[..7])
                        .Select(month => new CollectionTrendPoint(
                            month.Key,
                            month.All(point => point.Count.HasValue) ? month.Sum(point => point.Count!.Value) : null))
                        .ToList();
                }

                return new CollectionPublisherTrend(publisher.Key, points);
            })
            .ToList();
    }
}
