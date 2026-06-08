using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PulseBrief;

public sealed class ArticleStore
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private bool _initialized;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ArticleStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<List<Article>> ReadArticlesAsync()
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var articles = new List<Article>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, url, source, feed_url, summary, published_at, first_seen_at, updated_at, embedding_json
            FROM articles
            ORDER BY datetime(published_at) DESC, datetime(first_seen_at) DESC
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            articles.Add(ReadArticle(reader));
        }

        return articles;
    }

    public async Task<List<ArticleGroup>> ReadGroupsAsync()
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var groups = new List<ArticleGroup>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, category, article_ids_json, article_count, sources_json, latest_published_at,
                   score, seed_title, seed_summary, representative_title, summary
            FROM article_groups
            ORDER BY datetime(latest_published_at) DESC
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            groups.Add(new ArticleGroup
            {
                Id = reader.GetString(0),
                Category = reader.GetString(1),
                ArticleIds = JsonSerializer.Deserialize<string[]>(reader.GetString(2), _jsonOptions) ?? [],
                ArticleCount = reader.GetInt32(3),
                Sources = JsonSerializer.Deserialize<string[]>(reader.GetString(4), _jsonOptions) ?? [],
                LatestPublishedAt = DateTimeOffset.Parse(reader.GetString(5)),
                Score = reader.GetInt32(6),
                SeedTitle = reader.GetString(7),
                SeedSummary = reader.GetString(8),
                RepresentativeTitle = reader.GetString(9),
                Summary = reader.GetString(10)
            });
        }

        return groups;
    }

    public async Task<DailyIssueSummary?> ReadDailySummaryAsync(string date)
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT summary_json
            FROM daily_summaries
            WHERE date = $date
            """;
        command.Parameters.AddWithValue("$date", date);

        var json = await command.ExecuteScalarAsync() as string;
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<DailyIssueSummary>(json, _jsonOptions);
    }

    public async Task SaveDailySummaryAsync(DailyIssueSummary summary)
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO daily_summaries (date, generated_at, summary_json)
            VALUES ($date, $generatedAt, $summaryJson)
            ON CONFLICT(date) DO UPDATE SET
                generated_at = excluded.generated_at,
                summary_json = excluded.summary_json
            """;
        command.Parameters.AddWithValue("$date", summary.Date);
        command.Parameters.AddWithValue("$generatedAt", summary.GeneratedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$summaryJson", JsonSerializer.Serialize(summary, _jsonOptions));
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveArticlesAsync(IReadOnlyCollection<Article> articles)
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var article in articles)
        {
            await UpsertArticleAsync(connection, article);
        }

        await transaction.CommitAsync();
    }

    public async Task SaveGroupsAsync(IReadOnlyCollection<ArticleGroup> groups)
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM article_groups";
        await delete.ExecuteNonQueryAsync();

        foreach (var group in groups)
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO article_groups
                (id, category, article_ids_json, article_count, sources_json, latest_published_at,
                 score, seed_title, seed_summary, representative_title, summary)
                VALUES
                ($id, $category, $articleIds, $articleCount, $sources, $latestPublishedAt,
                 $score, $seedTitle, $seedSummary, $representativeTitle, $summary)
                """;
            command.Parameters.AddWithValue("$id", group.Id);
            command.Parameters.AddWithValue("$category", group.Category);
            command.Parameters.AddWithValue("$articleIds", JsonSerializer.Serialize(group.ArticleIds, _jsonOptions));
            command.Parameters.AddWithValue("$articleCount", group.ArticleCount);
            command.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(group.Sources, _jsonOptions));
            command.Parameters.AddWithValue("$latestPublishedAt", group.LatestPublishedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$score", group.Score);
            command.Parameters.AddWithValue("$seedTitle", group.SeedTitle);
            command.Parameters.AddWithValue("$seedSummary", group.SeedSummary);
            command.Parameters.AddWithValue("$representativeTitle", group.RepresentativeTitle);
            command.Parameters.AddWithValue("$summary", group.Summary);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<List<Article>> UpsertArticlesAsync(IReadOnlyCollection<Article> incoming)
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var article in incoming)
        {
            var existing = await FindArticleAsync(connection, article.Id);
            if (existing is not null)
            {
                article.Embedding ??= existing.Embedding;
                article.FirstSeenAt = existing.FirstSeenAt;
            }

            article.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertArticleAsync(connection, article);
        }

        await transaction.CommitAsync();
        return await ReadArticlesAsync();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _lock.WaitAsync();
        try
        {
            if (_initialized) return;

            Directory.CreateDirectory(_paths.DataDirectory);
            await using var connection = OpenConnection();
            await connection.OpenAsync();

            await CreateSchemaAsync(connection);
            await MigrateJsonDataIfNeededAsync(connection);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private SqliteConnection OpenConnection()
    {
        return new SqliteConnection($"Data Source={_paths.DatabasePath}");
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS articles (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                source TEXT NOT NULL,
                feed_url TEXT NOT NULL,
                summary TEXT NOT NULL,
                published_at TEXT NOT NULL,
                first_seen_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                embedding_json TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_articles_published_at ON articles(published_at DESC);
            CREATE INDEX IF NOT EXISTS idx_articles_source ON articles(source);

            CREATE TABLE IF NOT EXISTS article_groups (
                id TEXT PRIMARY KEY,
                category TEXT NOT NULL,
                article_ids_json TEXT NOT NULL,
                article_count INTEGER NOT NULL,
                sources_json TEXT NOT NULL,
                latest_published_at TEXT NOT NULL,
                score INTEGER NOT NULL,
                seed_title TEXT NOT NULL,
                seed_summary TEXT NOT NULL,
                representative_title TEXT NOT NULL,
                summary TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_article_groups_category ON article_groups(category);
            CREATE INDEX IF NOT EXISTS idx_article_groups_latest ON article_groups(latest_published_at DESC);

            CREATE TABLE IF NOT EXISTS daily_summaries (
                date TEXT PRIMARY KEY,
                generated_at TEXT NOT NULL,
                summary_json TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task MigrateJsonDataIfNeededAsync(SqliteConnection connection)
    {
        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM articles";
        var articleCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync());
        if (articleCount > 0) return;

        if (File.Exists(_paths.ArticlesPath))
        {
            await using var stream = File.OpenRead(_paths.ArticlesPath);
            var articles = await JsonSerializer.DeserializeAsync<List<Article>>(stream, _jsonOptions) ?? [];
            foreach (var article in articles)
            {
                await UpsertArticleAsync(connection, article);
            }
        }

        if (File.Exists(_paths.GroupsPath))
        {
            await using var stream = File.OpenRead(_paths.GroupsPath);
            var groups = await JsonSerializer.DeserializeAsync<List<ArticleGroup>>(stream, _jsonOptions) ?? [];
            foreach (var group in groups)
            {
                var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT OR REPLACE INTO article_groups
                    (id, category, article_ids_json, article_count, sources_json, latest_published_at,
                     score, seed_title, seed_summary, representative_title, summary)
                    VALUES
                    ($id, $category, $articleIds, $articleCount, $sources, $latestPublishedAt,
                     $score, $seedTitle, $seedSummary, $representativeTitle, $summary)
                    """;
                command.Parameters.AddWithValue("$id", group.Id);
                command.Parameters.AddWithValue("$category", group.Category);
                command.Parameters.AddWithValue("$articleIds", JsonSerializer.Serialize(group.ArticleIds, _jsonOptions));
                command.Parameters.AddWithValue("$articleCount", group.ArticleCount);
                command.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(group.Sources, _jsonOptions));
                command.Parameters.AddWithValue("$latestPublishedAt", group.LatestPublishedAt.ToUniversalTime().ToString("O"));
                command.Parameters.AddWithValue("$score", group.Score);
                command.Parameters.AddWithValue("$seedTitle", group.SeedTitle);
                command.Parameters.AddWithValue("$seedSummary", group.SeedSummary);
                command.Parameters.AddWithValue("$representativeTitle", group.RepresentativeTitle);
                command.Parameters.AddWithValue("$summary", group.Summary);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task<Article?> FindArticleAsync(SqliteConnection connection, string id)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, url, source, feed_url, summary, published_at, first_seen_at, updated_at, embedding_json
            FROM articles
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadArticle(reader) : null;
    }

    private async Task UpsertArticleAsync(SqliteConnection connection, Article article)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO articles
            (id, title, url, source, feed_url, summary, published_at, first_seen_at, updated_at, embedding_json)
            VALUES
            ($id, $title, $url, $source, $feedUrl, $summary, $publishedAt, $firstSeenAt, $updatedAt, $embedding)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                url = excluded.url,
                source = excluded.source,
                feed_url = excluded.feed_url,
                summary = excluded.summary,
                published_at = excluded.published_at,
                first_seen_at = articles.first_seen_at,
                updated_at = excluded.updated_at,
                embedding_json = COALESCE(excluded.embedding_json, articles.embedding_json)
            """;
        command.Parameters.AddWithValue("$id", article.Id);
        command.Parameters.AddWithValue("$title", article.Title);
        command.Parameters.AddWithValue("$url", article.Url);
        command.Parameters.AddWithValue("$source", article.Source);
        command.Parameters.AddWithValue("$feedUrl", article.FeedUrl);
        command.Parameters.AddWithValue("$summary", article.Summary);
        command.Parameters.AddWithValue("$publishedAt", article.PublishedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$firstSeenAt", article.FirstSeenAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", article.UpdatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$embedding", article.Embedding is null ? DBNull.Value : JsonSerializer.Serialize(article.Embedding, _jsonOptions));
        await command.ExecuteNonQueryAsync();
    }

    private Article ReadArticle(SqliteDataReader reader)
    {
        var embeddingJson = reader.IsDBNull(9) ? null : reader.GetString(9);
        return new Article
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Url = reader.GetString(2),
            Source = reader.GetString(3),
            FeedUrl = reader.GetString(4),
            Summary = reader.GetString(5),
            PublishedAt = DateTimeOffset.Parse(reader.GetString(6)),
            FirstSeenAt = DateTimeOffset.Parse(reader.GetString(7)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(8)),
            Embedding = string.IsNullOrWhiteSpace(embeddingJson)
                ? null
                : JsonSerializer.Deserialize<double[]>(embeddingJson, _jsonOptions)
        };
    }
}
