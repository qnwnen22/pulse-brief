namespace PulseBrief;

public sealed class CollectionStatisticsResponse
{
    public string Period { get; set; } = "7";
    public string TodayDate { get; set; } = "";
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public bool IsReady { get; set; }
    public int CompletedDays { get; set; }
    public int ExpectedDays { get; set; }
    public long? Total { get; set; }
    public double? DailyAverage { get; set; }
    public long? TodayCount { get; set; }
    public long? PreviousTotal { get; set; }
    public string? PreviousFromDate { get; set; }
    public string? PreviousToDate { get; set; }
    public double? ChangePercent { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<CollectionTrendPoint> Trend { get; set; } = [];
    public List<CollectionPublisherShare> Publishers { get; set; } = [];
    public List<CollectionPublisherTrend> PublisherTrends { get; set; } = [];
    public List<CollectionWeekdayStatistics> Weekdays { get; set; } = [];
    public bool ArePublisherCategoriesReady { get; set; }
    public int PublisherCategoryCompletedDays { get; set; }
    public List<CollectionPublisherCategories> PublisherCategories { get; set; } = [];
    public bool AreCategoriesReady { get; set; }
    public bool IsCategoryComparisonReady { get; set; }
    public int CategoryCompletedDays { get; set; }
    public List<CollectionCategoryShare> Categories { get; set; } = [];
}

public sealed record CollectionTrendPoint(string Date, long? Count);
public sealed record CollectionPublisherShare(string Publisher, long Count, double Share);
public sealed record CollectionPublisherTrend(string Publisher, List<CollectionTrendPoint> Trend);
public sealed record CollectionWeekdayStatistics(int Day, string Label, int SampleDays, int ExpectedDays, long? Total, double? Average);
public sealed record CollectionPublisherCategories(string Publisher, long Total, List<CollectionCategoryShare> Categories);
public sealed record CollectionCategoryShare(string Category, long Count, double Share)
{
    public long? PreviousCount { get; init; }
    public double? ChangePercent { get; init; }
    public double? ShareChangePoints { get; init; }
}
