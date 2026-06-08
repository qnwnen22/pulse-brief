using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PulseBrief;

/// <summary>SQLite 파일을 사용해 기사, 이슈 그룹, 요약 데이터를 읽고 쓰는 보조 저장소입니다.</summary>
public sealed class ArticleStore : IArticleStore
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

    /// <summary>애플리케이션 경로 설정을 받아 SQLite 데이터베이스 위치를 초기화합니다.</summary>
    public ArticleStore(AppPaths paths)
    {
        _paths = paths;
    }

    /// <summary>SQLite에 저장된 전체 기사 목록을 최신 발행 순으로 조회합니다.</summary>
    public async Task<List<Article>> ReadArticlesAsync()
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var articles = new List<Article>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, url, source, feed_url, summary, content, content_fetched_at, content_fetch_status,
                   content_fetch_error, published_at, first_seen_at, updated_at, embedding_json
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

    /// <summary>SQLite에 저장된 전체 이슈 그룹 목록을 최신 발행 순으로 조회합니다.</summary>
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

    /// <summary>날짜 또는 주간 키에 해당하는 요약 JSON을 SQLite에서 조회합니다.</summary>
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

    /// <summary>SQLite에 저장된 모든 요약 JSON을 생성 시각 역순으로 조회합니다.</summary>
    public async Task<List<DailyIssueSummary>> ReadDailySummariesAsync()
    {
        await EnsureInitializedAsync();
        await using var connection = OpenConnection();
        await connection.OpenAsync();

        var summaries = new List<DailyIssueSummary>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT summary_json
            FROM daily_summaries
            ORDER BY generated_at DESC
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            var summary = JsonSerializer.Deserialize<DailyIssueSummary>(json, _jsonOptions);
            if (summary is not null) summaries.Add(summary);
        }

        return summaries;
    }

    /// <summary>요약 문서를 날짜 키 기준으로 SQLite에 저장하거나 갱신합니다.</summary>
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

    /// <summary>기사 목록을 SQLite에 저장하거나 기존 기사 데이터를 갱신합니다.</summary>
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

    /// <summary>SQLite의 이슈 그룹 테이블을 현재 계산 결과로 교체합니다.</summary>
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

    /// <summary>새로 수집된 기사와 기존 SQLite 기사 데이터를 병합한 뒤 전체 기사 목록을 반환합니다.</summary>
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
            article.Content = existing.Content;
            article.ContentFetchedAt = existing.ContentFetchedAt;
            article.ContentFetchStatus = existing.ContentFetchStatus;
            article.ContentFetchError = existing.ContentFetchError;
        }

            article.UpdatedAt = DateTimeOffset.UtcNow;
            await UpsertArticleAsync(connection, article);
        }

        await transaction.CommitAsync();
        return await ReadArticlesAsync();
    }

    /// <summary>SQLite 데이터 디렉터리와 스키마를 최초 1회 준비하고 필요한 마이그레이션을 수행합니다.</summary>
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
            await EnsureArticleContentColumnsAsync(connection);
            await MigrateJsonDataIfNeededAsync(connection);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>현재 SQLite 데이터베이스 파일에 대한 새 연결 객체를 만듭니다.</summary>
    private SqliteConnection OpenConnection()
    {
        return new SqliteConnection($"Data Source={_paths.DatabasePath}");
    }

    /// <summary>기사, 그룹, 요약 테이블과 기본 인덱스를 생성합니다.</summary>
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
                content TEXT NOT NULL DEFAULT '',
                content_fetched_at TEXT,
                content_fetch_status TEXT NOT NULL DEFAULT '',
                content_fetch_error TEXT NOT NULL DEFAULT '',
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

    /// <summary>기존 SQLite DB에 기사 본문 수집 관련 컬럼이 없으면 추가합니다.</summary>
    private static async Task EnsureArticleContentColumnsAsync(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readColumns = connection.CreateCommand();
        readColumns.CommandText = "PRAGMA table_info(articles)";
        await using (var reader = await readColumns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(1));
            }
        }

        var migrations = new (string Column, string Sql)[]
        {
            ("content", "ALTER TABLE articles ADD COLUMN content TEXT NOT NULL DEFAULT ''"),
            ("content_fetched_at", "ALTER TABLE articles ADD COLUMN content_fetched_at TEXT"),
            ("content_fetch_status", "ALTER TABLE articles ADD COLUMN content_fetch_status TEXT NOT NULL DEFAULT ''"),
            ("content_fetch_error", "ALTER TABLE articles ADD COLUMN content_fetch_error TEXT NOT NULL DEFAULT ''")
        };

        foreach (var migration in migrations)
        {
            if (existing.Contains(migration.Column)) continue;
            var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>초기 JSON 파일이 남아 있고 SQLite가 비어 있을 때 기사/그룹 데이터를 가져옵니다.</summary>
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

    /// <summary>SQLite에서 기사 ID와 일치하는 기존 기사 문서를 조회합니다.</summary>
    private async Task<Article?> FindArticleAsync(SqliteConnection connection, string id)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, url, source, feed_url, summary, content, content_fetched_at, content_fetch_status,
                   content_fetch_error, published_at, first_seen_at, updated_at, embedding_json
            FROM articles
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadArticle(reader) : null;
    }

    /// <summary>단일 기사 문서를 SQLite articles 테이블에 저장하거나 기존 행을 갱신합니다.</summary>
    private async Task UpsertArticleAsync(SqliteConnection connection, Article article)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO articles
            (id, title, url, source, feed_url, summary, content, content_fetched_at, content_fetch_status,
             content_fetch_error, published_at, first_seen_at, updated_at, embedding_json)
            VALUES
            ($id, $title, $url, $source, $feedUrl, $summary, $content, $contentFetchedAt, $contentFetchStatus,
             $contentFetchError, $publishedAt, $firstSeenAt, $updatedAt, $embedding)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                url = excluded.url,
                source = excluded.source,
                feed_url = excluded.feed_url,
                summary = excluded.summary,
                content = CASE WHEN excluded.content <> '' THEN excluded.content ELSE articles.content END,
                content_fetched_at = COALESCE(excluded.content_fetched_at, articles.content_fetched_at),
                content_fetch_status = CASE WHEN excluded.content_fetch_status <> '' THEN excluded.content_fetch_status ELSE articles.content_fetch_status END,
                content_fetch_error = CASE WHEN excluded.content_fetch_error <> '' THEN excluded.content_fetch_error ELSE articles.content_fetch_error END,
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
        command.Parameters.AddWithValue("$content", article.Content);
        command.Parameters.AddWithValue("$contentFetchedAt", article.ContentFetchedAt is null ? DBNull.Value : article.ContentFetchedAt.Value.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$contentFetchStatus", article.ContentFetchStatus);
        command.Parameters.AddWithValue("$contentFetchError", article.ContentFetchError);
        command.Parameters.AddWithValue("$publishedAt", article.PublishedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$firstSeenAt", article.FirstSeenAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", article.UpdatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$embedding", article.Embedding is null ? DBNull.Value : JsonSerializer.Serialize(article.Embedding, _jsonOptions));
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>SQLite 조회 결과 한 행을 Article 모델로 변환합니다.</summary>
    private Article ReadArticle(SqliteDataReader reader)
    {
        DateTimeOffset? contentFetchedAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7));
        var embeddingJson = reader.IsDBNull(13) ? null : reader.GetString(13);
        return new Article
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Url = reader.GetString(2),
            Source = reader.GetString(3),
            FeedUrl = reader.GetString(4),
            Summary = reader.GetString(5),
            Content = reader.GetString(6),
            ContentFetchedAt = contentFetchedAt,
            ContentFetchStatus = reader.GetString(8),
            ContentFetchError = reader.GetString(9),
            PublishedAt = DateTimeOffset.Parse(reader.GetString(10)),
            FirstSeenAt = DateTimeOffset.Parse(reader.GetString(11)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(12)),
            Embedding = string.IsNullOrWhiteSpace(embeddingJson)
                ? null
                : JsonSerializer.Deserialize<double[]>(embeddingJson, _jsonOptions)
        };
    }
}
