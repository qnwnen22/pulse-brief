namespace PulseBrief;

public sealed record ManualSummaryArticle(
    string Id,
    string Title,
    string Url,
    string Source,
    string Author,
    string Summary,
    string Content,
    DateTimeOffset PublishedAt,
    DateTimeOffset? FirstSeenAt);

public sealed record ManualSummaryArticleExport(
    string Status,
    string Date,
    DateTimeOffset SnapshotAt,
    DateTimeOffset StartInclusive,
    DateTimeOffset EndExclusive,
    bool Complete,
    int ArticleCount,
    int TotalArticleCount,
    int ExportedArticleCount,
    string? NextCursor,
    string? Sha256,
    IReadOnlyList<ManualSummaryArticle> Articles);

public sealed record ManualSummaryPublication(
    string Status,
    string Date,
    bool Matches,
    string Sha256);
