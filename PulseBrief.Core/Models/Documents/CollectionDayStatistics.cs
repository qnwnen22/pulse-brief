using MongoDB.Bson.Serialization.Attributes;

namespace PulseBrief;

[BsonIgnoreExtraElements]
public sealed class CollectionDayStatistics
{
    public string Id { get; set; } = "";
    public Dictionary<string, long> Publishers { get; set; } = new();
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
