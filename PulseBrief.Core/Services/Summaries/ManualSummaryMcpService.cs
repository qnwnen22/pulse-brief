using System.Security.Cryptography;
using System.Text.Json;

namespace PulseBrief;

/// <summary>
/// Narrow article-export and insert-only publication contract for the authenticated MCP adapter.
/// </summary>
public sealed class ManualSummaryMcpService(IArticleStore store, IConfiguration configuration)
{
    public const int DefaultArticleLimit = 10000;
    private const int MaxSummaryBytes = 2 * 1024 * 1024;
    public const int DefaultExportPageSize = 100;
    private const int MaxExportPageBytes = 1024 * 1024;
    private const int MaxExportArticleTextCharacters = MaxExportPageBytes / 6;
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "정치/정책", "경제/산업", "사회", "국제", "IT/과학", "문화/연예", "스포츠", "생활/건강", "지역"
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ManualSummaryArticleExport> ExportDailyArticlesAsync(
        string date, int maxArticles = DefaultArticleLimit, string? cursor = null, CancellationToken cancellationToken = default)
    {
        RequireEnabled();
        var target = ParseDate(date);
        if (maxArticles is < 1 or > DefaultArticleLimit) throw new ArgumentOutOfRangeException(nameof(maxArticles));

        if (await store.ReadDailySummaryAsync(date) is not null)
        {
            return new ManualSummaryArticleExport("existing", date, DateTimeOffset.UtcNow, Start(date), Start(date).AddDays(1), true, 0, 0, 0, null, null, []);
        }

        ExportCursor state;
        if (cursor is null)
        {
            var snapshotAt = DateTimeOffset.UtcNow;
            var totalCount = await store.CountArticlesForManualSummaryAsync(target, snapshotAt, cancellationToken);
            state = CreateInitialCursor(date, maxArticles, snapshotAt, totalCount);
        }
        else
        {
            state = DecodeCursor(cursor, date, maxArticles);
        }
        if (state.SnapshotAt < DateTimeOffset.UtcNow.AddDays(-1)
            || state.SnapshotAt > DateTimeOffset.UtcNow.AddSeconds(5))
            throw new InvalidOperationException("Export cursor expired; restart the full export from its first page.");
        var currentTotal = await store.CountArticlesForManualSummaryAsync(target, state.SnapshotAt, cancellationToken);
        if (currentTotal != state.TotalArticleCount)
            throw new InvalidOperationException("Article set changed during the export snapshot; restart the full export.");

        var articles = new List<ManualSummaryArticle>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("["u8);
        var payloadBytes = 2; // JSON array brackets
        var hasMore = false;
        await foreach (var row in store.StreamArticlesForManualSummaryAsync(
                target, state.SnapshotAt, state.LastPublishedAt, state.LastId, DefaultExportPageSize + 1, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            if (articles.Count >= DefaultExportPageSize)
            {
                hasMore = true;
                break;
            }
            if (string.IsNullOrWhiteSpace(row.Id) || !ids.Add(row.Id))
                throw new InvalidOperationException("Article IDs are missing or duplicated; export is incomplete.");
            if (!IsInDate(row.PublishedAt, date))
                throw new InvalidOperationException("Date range export returned an article outside the requested day.");

            var article = new ManualSummaryArticle(
                row.Id, row.Title, row.Url, row.Source, row.Author, row.Summary,
                row.Content, row.PublishedAt, row.FirstSeenAt);
            var textCharacters = article.Id.Length + article.Title.Length + article.Url.Length
                + article.Source.Length + article.Author.Length + article.Summary.Length + article.Content.Length;
            if (textCharacters > MaxExportArticleTextCharacters)
                throw new InvalidOperationException("An article exceeds the 1 MiB page safety limit.");
            var itemBytes = JsonSerializer.SerializeToUtf8Bytes(article, JsonOptions);
            var separatorBytes = articles.Count == 0 ? 0 : 1;
            if (itemBytes.Length + 2 > MaxExportPageBytes)
                throw new InvalidOperationException("An article exceeds the 1 MiB page safety limit.");
            if (payloadBytes + separatorBytes + itemBytes.Length > MaxExportPageBytes)
            {
                hasMore = true;
                break;
            }

            if (separatorBytes != 0)
            {
                hash.AppendData(","u8);
                payloadBytes += separatorBytes;
            }
            hash.AppendData(itemBytes);
            payloadBytes += itemBytes.Length;
            articles.Add(article);
        }
        hash.AppendData("]"u8);

        if (articles.Count == 0 && state.TotalArticleCount != 0)
            throw new InvalidOperationException("The article snapshot ended before all pages were exported.");
        var exportedCount = state.ExportedArticleCount + articles.Count;
        if (exportedCount > state.TotalArticleCount || (hasMore && exportedCount >= state.TotalArticleCount)
            || (!hasMore && exportedCount != state.TotalArticleCount))
            throw new InvalidOperationException("Export page totals do not match the stable article count.");
        var nextCursor = hasMore
            ? EncodeCursor(state with
            {
                ExportedArticleCount = exportedCount,
                LastPublishedAt = articles[^1].PublishedAt,
                LastId = articles[^1].Id
            })
            : null;

        return new ManualSummaryArticleExport(
            "exported", date, state.SnapshotAt, Start(date), Start(date).AddDays(1), !hasMore, articles.Count,
            state.TotalArticleCount, exportedCount, nextCursor,
            Convert.ToHexStringLower(hash.GetHashAndReset()), articles);
    }

    private static ExportCursor CreateInitialCursor(string date, int maxArticles, DateTimeOffset snapshotAt, long totalCount)
    {
        if (totalCount > maxArticles)
            throw new InvalidOperationException($"The date has {totalCount} eligible articles, above the {maxArticles} article ceiling.");
        return new ExportCursor(1, date, maxArticles, snapshotAt, (int)totalCount, 0, null, null);
    }

    private static string EncodeCursor(ExportCursor state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static ExportCursor DecodeCursor(string token, string date, int maxArticles)
    {
        if (token.Length is < 1 or > 8192) throw new ArgumentException("cursor length is invalid.", nameof(token));
        try
        {
            var base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var state = JsonSerializer.Deserialize<ExportCursor>(Convert.FromBase64String(base64), JsonOptions);
            if (state is null || state.Version != 1 || state.Date != date || state.MaxArticles != maxArticles
                || state.SnapshotAt == default || state.TotalArticleCount is < 1 or > DefaultArticleLimit
                || state.ExportedArticleCount is < 1 || state.ExportedArticleCount >= state.TotalArticleCount
                || state.LastPublishedAt is null || state.LastId is null || state.LastId.Length > 300
                || !IsInDate(state.LastPublishedAt.Value, date))
                throw new ArgumentException("cursor does not match this date or export.", nameof(token));
            return state;
        }
        catch (FormatException error) { throw new ArgumentException("cursor encoding is invalid.", nameof(token), error); }
        catch (JsonException error) { throw new ArgumentException("cursor format is invalid.", nameof(token), error); }
    }

    private sealed record ExportCursor(
        int Version,
        string Date,
        int MaxArticles,
        DateTimeOffset SnapshotAt,
        int TotalArticleCount,
        int ExportedArticleCount,
        DateTimeOffset? LastPublishedAt,
        string? LastId);

    public async Task<ManualSummaryPublication> PublishDailySummaryAsync(
        string date, DailyIssueSummary summary, CancellationToken cancellationToken = default)
    {
        RequireEnabled();
        _ = ParseDate(date);
        ValidateSummary(date, summary);

        var requestedHash = Hash(summary);
        var ids = summary.TopIssues.SelectMany(issue => issue.ArticleIds).Distinct(StringComparer.Ordinal).ToArray();
        var referencedArticles = await store.ReadArticlesByIdsAsync(ids);
        var byId = referencedArticles.ToDictionary(article => article.Id, StringComparer.Ordinal);
        if (byId.Count != ids.Length || ids.Any(id => !byId.TryGetValue(id, out var article)
            || article.IsExcluded || !IsInDate(article.PublishedAt, date)))
            throw new InvalidOperationException("Summary evidence contains a missing, excluded, or out-of-period article.");

        var result = await store.TryInsertManualDailySummaryAsync(summary, cancellationToken);
        var matches = requestedHash == Hash(result.Summary);
        return new ManualSummaryPublication(result.Inserted ? "published" : "existing", date, matches, Hash(result.Summary));
    }

    private void RequireEnabled()
    {
        if (!configuration.GetValue("Mcp:DailySummary:Enabled", false))
            throw new InvalidOperationException("Manual summary MCP tools are disabled.");
    }

    private static DateOnly ParseDate(string date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed)
            || parsed.ToString("yyyy-MM-dd") != date
            || parsed > KoreaDate.Today().AddDays(-1))
            throw new ArgumentException("date must be a past yyyy-MM-dd Korea date.", nameof(date));
        return parsed;
    }

    private static DateTimeOffset Start(string date) => KoreaDate.StartOfDay(DateOnly.ParseExact(date, "yyyy-MM-dd"));

    private static bool IsInDate(DateTimeOffset publishedAt, string date)
    {
        var start = Start(date);
        return publishedAt >= start && publishedAt < start.AddDays(1);
    }

    private static void ValidateSummary(string date, DailyIssueSummary summary)
    {
        static void Need(bool condition, string message)
        {
            if (!condition) throw new ArgumentException(message, "summary");
        }

        static void Text(string? value, int max, string name) => Need(!string.IsNullOrWhiteSpace(value) && value.Length <= max, $"{name} is invalid.");
        static void UniqueStrings(string[]? values, int maxCount, string name)
        {
            Need(values is not null && values.Length <= maxCount, $"{name} is invalid.");
            foreach (var value in values!) Text(value, 300, name);
            Need(values!.Distinct(StringComparer.Ordinal).Count() == values.Length, $"{name} contains duplicates.");
        }

        if (summary is null) throw new ArgumentNullException(nameof(summary));
        Need(summary.Date == date && summary.Provider == "manual", "Summary date or provider is invalid.");
        Text(summary.Model, 100, "Model");
        Text(summary.Headline, 300, "Headline");
        Text(summary.Summary, 30000, "Summary");
        Need(summary.GeneratedAt != default, "GeneratedAt is invalid.");
        Need(summary.ArticleCount is > 0 and <= 50000, "ArticleCount is invalid.");
        Need(summary.IssueCount is > 0 and <= 50000, "IssueCount is invalid.");
        Need(summary.SourceCount is > 0 and <= 1000 && summary.SourceCount <= summary.ArticleCount, "SourceCount is invalid.");
        var categories = summary.Categories;
        Need(categories is { Length: > 0 and <= 9 }, "Categories are invalid.");
        if (categories is null) throw new ArgumentException("Categories are invalid.", "summary");
        Need(categories.Select(row => row.Category).All(Categories.Contains)
            && categories.Select(row => row.Category).Distinct(StringComparer.Ordinal).Count() == categories.Length,
            "Categories contain an unknown or duplicate category.");
        foreach (var category in categories)
        {
            Need(category.ArticleCount > 0 && category.IssueCount > 0 && category.IssueCount <= category.ArticleCount,
                "Category counts are invalid.");
            Text(category.Summary, 3000, "Category summary");
        }
        Need(categories.Sum(row => row.ArticleCount) == summary.ArticleCount
            && categories.Sum(row => row.IssueCount) == summary.IssueCount, "Category totals do not match the summary.");
        var issues = summary.TopIssues;
        Need(issues is { Length: > 0 and <= 27 }, "TopIssues are invalid.");
        if (issues is null) throw new ArgumentException("TopIssues are invalid.", "summary");

        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var issue in issues)
        {
            Need(Categories.Contains(issue.Category) && categories.Any(row => row.Category == issue.Category), "Issue category is invalid.");
            Text(issue.Title, 300, "Issue title");
            Text(issue.Summary, 3000, "Issue summary");
            Need(issue.ArticleCount is > 0 and <= 50000 && issue.ArticleIds is not null
                && issue.ArticleIds.Length == issue.ArticleCount, "Issue evidence count is invalid.");
            foreach (var id in issue.ArticleIds ?? [])
            {
                Text(id, 300, "Article ID");
                Need(allIds.Add(id), "Article IDs are duplicated across issues.");
            }
            Need(issue.Score is >= 0 and <= 100, "Issue score is invalid.");
            UniqueStrings(issue.Sources, 1000, "Sources");
            UniqueStrings(issue.Keywords, 15, "Keywords");
        }
        foreach (var category in categories)
        {
            var own = issues.Where(issue => issue.Category == category.Category).ToArray();
            Need(own.Length <= 3 && own.Length <= category.IssueCount
                && own.Sum(issue => issue.ArticleCount) <= category.ArticleCount,
                "Featured issue counts exceed the category totals.");
        }
        Need(JsonSerializer.SerializeToUtf8Bytes(summary, JsonOptions).Length <= MaxSummaryBytes, "Summary exceeds the 2 MiB limit.");
    }

    private static string Hash(DailyIssueSummary summary) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(summary, JsonOptions)));
}
