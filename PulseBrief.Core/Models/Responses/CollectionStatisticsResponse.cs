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
    public double? ChangePercent { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<CollectionTrendPoint> Trend { get; set; } = [];
    public List<CollectionPublisherShare> Publishers { get; set; } = [];
}

public sealed record CollectionTrendPoint(string Date, long? Count);
public sealed record CollectionPublisherShare(string Publisher, long Count, double Share);
