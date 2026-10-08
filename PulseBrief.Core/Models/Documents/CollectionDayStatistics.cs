using MongoDB.Bson.Serialization.Attributes;

namespace PulseBrief;

[BsonIgnoreExtraElements]
public sealed class CollectionDayStatistics
{
    public string Id { get; set; } = "";
    public Dictionary<string, long> Publishers { get; set; } = new();
    public Dictionary<string, long> Categories { get; set; } = new();
    public int CategoryVersion { get; set; }
    public Dictionary<string, Dictionary<string, long>> PublisherCategories { get; set; } = new();
    public int PublisherCategoryVersion { get; set; }
    public long ArticleCount { get; set; }
    public bool IsComplete { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class CollectionStatisticsState
{
    public string Id { get; set; } = "public";
    public string FirstDate { get; set; } = "";
}
