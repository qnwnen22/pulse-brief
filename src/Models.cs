using System.Text.Json.Serialization;

namespace PulseBrief;

public sealed class Article
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Source { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public double[]? Embedding { get; set; }
}

public sealed class ArticleGroup
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "사회";
    public string[] ArticleIds { get; set; } = [];
    public int ArticleCount { get; set; }
    public string[] Sources { get; set; } = [];
    public DateTimeOffset LatestPublishedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Score { get; set; }
    public string SeedTitle { get; set; } = "";
    public string SeedSummary { get; set; } = "";
    public string RepresentativeTitle { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class BriefDto
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Source { get; set; } = "";
    public int Minutes { get; set; }
    public int Impact { get; set; }
    public string Heat { get; set; } = "normal";
    public string Summary { get; set; } = "";
    public string[] Keywords { get; set; } = [];
    public int ArticleCount { get; set; }
    public string[] ArticleIds { get; set; } = [];
    public DateTimeOffset LatestPublishedAt { get; set; }
    public RelatedLinkDto[] RelatedLinks { get; set; } = [];
}

public sealed class RelatedLinkDto
{
    public string Title { get; set; } = "";
    public string Source { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed record PipelineResult(int FetchedCount, int ArticleCount, int GroupCount, DateTimeOffset UpdatedAt);
