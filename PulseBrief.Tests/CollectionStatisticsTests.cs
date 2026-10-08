using MongoDB.Bson;
using MongoDB.Bson.Serialization;
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
            Publishers = new() { ["연합뉴스"] = offset <= 7 ? 12 : 6, ["한겨레"] = offset <= 7 ? 8 : 4 },
            Categories = new() { ["정치/정책"] = offset == 0 ? 1000 : offset <= 7 ? 12 : 6, ["사회"] = offset == 0 ? 0 : offset <= 7 ? 8 : 4 },
            CategoryVersion = ArticleCategoryClassifier.StatisticsVersion
        }).ToList();
        var result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.IsReady && result.Total == 140 && result.DailyAverage == 20, "Today's unfinished day leaked into averages.");
        check(result.TodayCount == 1000 && result.PreviousTotal == 70 && result.ChangePercent == 100, "Equal-length period comparison failed.");
        check(result.Publishers[0] is { Publisher: "연합뉴스", Count: 84, Share: 60 }, "Publisher counts or shares are incorrect.");
        check(result.Publishers.Sum(item => item.Count) == result.Total, "Publisher count sum differs from total.");
        check(result.AreCategoriesReady && result.Categories.Count == 9 && result.Categories[0] is { Category: "정치/정책", Count: 84, Share: 60 }, "Category counts or shares are incorrect.");
        check(result.Categories.Sum(item => item.Count) == result.Total, "Category totals must count every saved article exactly once.");
        check(result.Categories.Any(item => item.Count == 0 && item.Share == 0), "Zero-article categories must remain available.");
        check(result.IsCategoryComparisonReady && result.Categories[0] is { PreviousCount: 42, ChangePercent: 100, ShareChangePoints: 0 }, "Category comparison must use an equal-length previous period.");
        check(result.PreviousFromDate == KoreaDate.Key(today.AddDays(-14)) && result.PreviousToDate == KoreaDate.Key(today.AddDays(-8)), "Previous period date boundaries are incorrect.");
        check(result.Categories.Single(item => item.Category == "스포츠") is { PreviousCount: 0, ChangePercent: null, ShareChangePoints: 0 }, "Zero-category comparison generated a percentage.");
        check(result.PublisherTrends.Count == 2 && result.PublisherTrends[0].Trend.Count == 7
            && result.PublisherTrends[0].Trend.Sum(point => point.Count) == 84, "Publisher trends must reuse current-period day counts.");
        check(result.PublisherTrends[0].Trend[0].Date == KoreaDate.Key(today.AddDays(-7))
            && result.PublisherTrends[0].Trend[^1].Count == 12, "Today or previous-period articles leaked into publisher trends.");
        var all = CollectionStatisticsService.Calculate("all", today, first, first, days);
        check(!all.IsCategoryComparisonReady && all.Categories.All(item => item.PreviousCount is null), "All-history categories must not invent a previous period.");
        check(all.PublisherTrends[0].Trend.All(point => point.Date.Length == 7)
            && all.PublisherTrends[0].Trend.Sum(point => point.Count) == 126, "All-history publisher trends must be compact monthly sums.");
        days[8].CategoryVersion = 0;
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.AreCategoriesReady && !result.IsCategoryComparisonReady && result.PreviousTotal == 70,
            "Legacy previous categories must not hide current shares or fabricate category changes.");
        days[8].CategoryVersion = ArticleCategoryClassifier.StatisticsVersion;
        days[8].IsComplete = false;
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.IsReady && !result.IsCategoryComparisonReady && result.PreviousTotal is null, "Missing comparison days must not count as zero.");
        days[8].IsComplete = true;
        days[1].Categories = new() { ["정치/정책"] = 8, ["사회"] = 12 };
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.Categories.Single(item => item.Category == "정치/정책") is { Count: 80, ChangePercent: 90.5, ShareChangePoints: -2.9 },
            "Percentage points must compare unrounded period shares, independently of count growth.");
        days[1].Categories = new() { ["정치/정책"] = 12, ["사회"] = 8 };

        days[1].CategoryVersion = 0;
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.IsReady && !result.AreCategoriesReady && result.CategoryCompletedDays == 6 && result.Categories.Count == 0, "Legacy publisher cache must not be mistaken for complete categories.");
        days[1].CategoryVersion = ArticleCategoryClassifier.StatisticsVersion;
        days[1].Categories["사회"]++;
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(!result.AreCategoriesReady && result.Total == 140, "Category-total mismatch must not corrupt existing statistics.");
        days[1].Categories["사회"]--;
        var legacy = BsonSerializer.Deserialize<CollectionDayStatistics>(new BsonDocument
        {
            { "_id", KoreaDate.Key(today.AddDays(-1)) }, { "ArticleCount", 20L }, { "IsComplete", true },
            { "Publishers", new BsonDocument("연합뉴스", 20L) }
        });
        check(legacy.CategoryVersion == 0 && !CollectionStatisticsService.HasCurrentCategories(legacy), "Existing BSON documents must deserialize without inventing category coverage.");
        days[1].Categories["사회"] = -1;
        check(!CollectionStatisticsService.HasCurrentCategories(days[1]), "Negative category counts must be rejected.");
        days[1].Categories["사회"] = 8;

        var zero = days.Single(day => day.Id == KoreaDate.Key(today.AddDays(-1)));
        zero.ArticleCount = 0;
        zero.Publishers.Clear();
        zero.Categories.Clear();
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(result.IsReady && result.Total == 120 && result.DailyAverage == 17.1 && result.ExpectedDays == 7, "Zero-article dates must be included in the average denominator.");
        check(result.AreCategoriesReady && result.Categories.Sum(item => item.Count) == 120, "Zero days broke category readiness.");
        zero.IsComplete = false;
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, days);
        check(!result.IsReady && result.Total is null && result.DailyAverage is null && result.Publishers.Count == 0, "Incomplete cache was presented as a complete period.");
        check(result.Trend[^1].Count is null && result.CompletedDays == 6, "Missing dates must not be treated as zero.");
        check(result.PublisherTrends.All(item => item.Trend[^1].Count is null), "Missing publisher dates must not become zero counts.");
        all = CollectionStatisticsService.Calculate("all", today, first, first, days);
        check(all.PublisherTrends.All(item => item.Trend[^1].Count is null), "An incomplete date must make its monthly publisher total unknown.");
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
        check(cached.Total == 70 && store.CacheReads == 1 && store.IndexInitializations == 0 && store.RefreshedDates.Count == 0, "Public statistics must compose changes and trends from one cache read, without article maintenance.");
        store.State = null;
        check(!(await service.ReadAsync("7", today, CancellationToken.None)).IsReady, "Absent cache did not return a waiting state.");
        store.State = new() { FirstDate = KoreaDate.Key(today.AddDays(-60)) };
        var missing = KoreaDate.Key(today.AddDays(-4));
        store.Days.RemoveAll(day => day.Id == missing || day.Id == KoreaDate.Key(today.AddDays(-5)));
        var maintenance = new CollectionStatisticsMaintenance(store);
        await maintenance.RefreshAsync(false, CancellationToken.None);
        check(store.RefreshedDates.Count == 2 && store.RefreshedDates[^1] == today.AddDays(-4), "Routine maintenance must repair at most one historical day and skip finalized yesterday.");
        check(store.IndexInitializations == 0, "Routine maintenance must not build indexes.");
        await maintenance.RefreshAsync(true, CancellationToken.None);
        check(store.IndexInitializations == 1 && store.Days.Any(day => day.Id == KoreaDate.Key(today.AddDays(-5)) && day.IsComplete), "Explicit backfill did not repair missing history.");
        check(store.Days.Select(day => day.Id).Distinct().Count() == store.Days.Count, "Repeated maintenance created duplicate dates.");
        store.Days.Single(day => day.Id == KoreaDate.Key(today.AddDays(-3))).CategoryVersion = 0;
        var before = store.RefreshedDates.Count;
        await maintenance.RefreshAsync(false, CancellationToken.None);
        check(store.RefreshedDates.Count == before + 2 && store.RefreshedDates[^1] == today.AddDays(-3), "Category schema migration must repair a legacy completed day within the history budget.");

        var examples = new Dictionary<string, string>
        {
            ["정치/정책"] = "국회 정부 법안", ["문화/연예"] = "영화 음악 공연", ["스포츠"] = "야구 축구 농구",
            ["경제/산업"] = "증시 코스피 금융", ["IT/과학"] = "소프트웨어 인공지능 로봇", ["국제"] = "미국 중국 러시아",
            ["생활/건강"] = "건강 질병 식품", ["지역"] = "부산 대구 울산", ["사회"] = "사건 사고 법원"
        };
        foreach (var (category, headline) in examples)
        {
            check(ArticleCategoryClassifier.ForMetadata("", headline, "") == category, $"Metadata classifier failed for {category}.");
            check(ArticleCategoryClassifier.ForArticles([new() { Title = headline }]) == category, $"Shared group classification changed for {category}.");
        }
        check(ArticleCategoryClassifier.ForMetadata("", "[ET포토] 대통령 정부 국회", "") == "문화/연예", "Photo priority changed.");
        check(ArticleCategoryClassifier.ForMetadata("", "중립 제목", "야구 축구") == "스포츠", "RSS summary was not used for category classification.");
        check(ArticleCategoryClassifier.ForArticles([new() { Content = "축구 야구 스포츠" }]) == "스포츠", "Existing group classification must retain article body input.");
        check(ArticleCategoryClassifier.ForMetadata("", "중립 제목", "") == "사회", "The established default category changed.");
        check(ArticleCategoryClassifier.ForMetadata("", new string('x', 800) + "축구", "") == "사회", "Metadata classification exceeded its title budget.");

        var emptyDays = Enumerable.Range(1, 7).Select(offset => new CollectionDayStatistics
        {
            Id = KoreaDate.Key(today.AddDays(-offset)), IsComplete = true, CategoryVersion = ArticleCategoryClassifier.StatisticsVersion
        }).ToList();
        var emptyResult = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, emptyDays);
        check(emptyResult.AreCategoriesReady && emptyResult.Categories.All(item => item.Share == 0), "Empty periods must not produce NaN or infinity category shares.");
        check(emptyResult.PublisherTrends.Count == 0, "Empty periods must not invent publisher series.");
        var zeroComparison = Enumerable.Range(8, 7).Select(offset => new CollectionDayStatistics
        {
            Id = KoreaDate.Key(today.AddDays(-offset)), IsComplete = true, CategoryVersion = ArticleCategoryClassifier.StatisticsVersion
        }).ToList();
        var current = Enumerable.Range(1, 7).Select(offset => new CollectionDayStatistics
        {
            Id = KoreaDate.Key(today.AddDays(-offset)), IsComplete = true, ArticleCount = 10,
            Categories = new() { ["사회"] = 10 }, Publishers = new() { ["한겨레"] = 10 }, CategoryVersion = ArticleCategoryClassifier.StatisticsVersion
        }).ToList();
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, [.. current, .. zeroComparison]);
        check(result.IsCategoryComparisonReady && result.Categories.All(item => item.PreviousCount == 0 && item.ChangePercent is null && item.ShareChangePoints is null),
            "Zero previous article totals must not yield percentages or percentage points.");
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, [.. emptyDays, .. zeroComparison]);
        check(result.IsCategoryComparisonReady && result.Categories.All(item => item.ShareChangePoints is null), "Two empty periods generated percentage-point changes.");
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), today.AddDays(-10), [.. current, .. zeroComparison]);
        check(!result.IsCategoryComparisonReady && result.Categories.All(item => item.PreviousCount is null), "Pre-collection dates must not be fabricated as complete comparisons.");
        current[0].Publishers = new() { ["연합뉴스"] = 10 };
        result = CollectionStatisticsService.Calculate("7", today, today.AddDays(-7), first, current);
        check(result.PublisherTrends.Single(item => item.Publisher == "한겨레").Trend[^1].Count == 0
            && result.PublisherTrends.Sum(item => item.Trend.Sum(point => point.Count)) == result.Total,
            "Absent publishers on complete days must be zero without losing total counts.");
    }
}
