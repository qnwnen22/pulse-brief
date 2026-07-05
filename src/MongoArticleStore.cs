using MongoDB.Driver;

namespace PulseBrief;

/// <summary>MongoDB를 기본 저장소로 사용해 기사, 이슈 그룹, 요약 데이터를 읽고 씁니다.</summary>
public sealed class MongoArticleStore : IArticleStore
{
    private readonly IMongoCollection<Article> _articles;
    private readonly IMongoCollection<ArticleGroup> _groups;
    private readonly IMongoCollection<DailyIssueSummary> _summaries;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>설정 또는 환경 변수에서 MongoDB 연결 정보를 읽어 컬렉션 핸들을 초기화합니다.</summary>
    public MongoArticleStore(IConfiguration configuration)
    {
        var connectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING")
            ?? configuration["Mongo:ConnectionString"]
            ?? "mongodb://127.0.0.1:27017";
        var databaseName = Environment.GetEnvironmentVariable("MONGO_DATABASE_NAME")
            ?? configuration["Mongo:DatabaseName"]
            ?? "pulsebrief";

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        _articles = database.GetCollection<Article>("articles");
        _groups = database.GetCollection<ArticleGroup>("articleGroups");
        _summaries = database.GetCollection<DailyIssueSummary>("summaries");
    }

    /// <summary>MongoDB에 저장된 전체 기사 목록을 최신 발행 순으로 조회합니다.</summary>
    public async Task<List<Article>> ReadArticlesAsync()
    {
        await EnsureInitializedAsync();
        return await _articles.Find(Builders<Article>.Filter.Empty)
            .SortByDescending(article => article.PublishedAt)
            .ThenByDescending(article => article.FirstSeenAt)
            .ToListAsync();
    }

    public async Task<List<Article>> ReadRecentArticlesAsync(int limit)
    {
        await EnsureInitializedAsync();
        return await _articles.Find(Builders<Article>.Filter.Empty)
            .SortByDescending(article => article.PublishedAt)
            .ThenByDescending(article => article.FirstSeenAt)
            .Limit(Math.Max(1, limit))
            .ToListAsync();
    }

    /// <summary>MongoDB에 저장된 전체 이슈 그룹 목록을 최신 발행 순으로 조회합니다.</summary>
    public async Task<List<ArticleGroup>> ReadGroupsAsync()
    {
        await EnsureInitializedAsync();
        return await _groups.Find(Builders<ArticleGroup>.Filter.Empty)
            .SortByDescending(group => group.LatestPublishedAt)
            .ToListAsync();
    }

    public async Task<List<ArticleGroup>> ReadRecentGroupsAsync(int limit)
    {
        await EnsureInitializedAsync();
        return await _groups.Find(Builders<ArticleGroup>.Filter.Empty)
            .SortByDescending(group => group.LatestPublishedAt)
            .Limit(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<List<Article>> ReadArticlesByIdsAsync(IReadOnlyCollection<string> ids)
    {
        await EnsureInitializedAsync();
        var normalizedIds = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedIds.Length == 0) return [];

        var filter = Builders<Article>.Filter.In(article => article.Id, normalizedIds);
        var projection = Builders<Article>.Projection
            .Exclude(article => article.Content)
            .Exclude(article => article.Embedding);

        return await _articles.Find(filter)
            .Project<Article>(projection)
            .ToListAsync();
    }

    /// <summary>날짜 또는 주간 키와 일치하는 요약 문서를 조회합니다.</summary>
    public async Task<DailyIssueSummary?> ReadDailySummaryAsync(string date)
    {
        await EnsureInitializedAsync();
        return await _summaries.Find(summary => summary.Date == date).FirstOrDefaultAsync();
    }

    /// <summary>저장된 일간/주간 요약 문서를 생성 시각 역순으로 조회합니다.</summary>
    public async Task<List<DailyIssueSummary>> ReadDailySummariesAsync()
    {
        await EnsureInitializedAsync();
        return await _summaries.Find(Builders<DailyIssueSummary>.Filter.Empty)
            .SortByDescending(summary => summary.GeneratedAt)
            .ToListAsync();
    }

    /// <summary>요약 문서를 date 필드 기준으로 upsert합니다.</summary>
    public async Task SaveDailySummaryAsync(DailyIssueSummary summary)
    {
        await EnsureInitializedAsync();
        await RemoveDuplicateSummariesAsync(summary.Date);
        await _summaries.ReplaceOneAsync(
            item => item.Date == summary.Date,
            summary,
            new ReplaceOptions { IsUpsert = true });
    }

    /// <summary>기사 문서 목록을 id 기준으로 upsert합니다.</summary>
    public async Task SaveArticlesAsync(IReadOnlyCollection<Article> articles)
    {
        await EnsureInitializedAsync();
        foreach (var article in articles)
        {
            await _articles.ReplaceOneAsync(
                item => item.Id == article.Id,
                article,
                new ReplaceOptions { IsUpsert = true });
        }
    }

    /// <summary>현재 이슈 그룹 컬렉션을 지우고 새 그룹 계산 결과로 교체합니다.</summary>
    public async Task SaveGroupsAsync(IReadOnlyCollection<ArticleGroup> groups)
    {
        await EnsureInitializedAsync();
        await _groups.DeleteManyAsync(Builders<ArticleGroup>.Filter.Empty);
        if (groups.Count > 0) await _groups.InsertManyAsync(groups);
    }

    /// <summary>새로 수집된 기사와 기존 MongoDB 기사 문서를 병합하고 전체 기사 목록을 반환합니다.</summary>
    public async Task<List<Article>> UpsertArticlesAsync(IReadOnlyCollection<Article> incoming)
    {
        await EnsureInitializedAsync();
        foreach (var article in incoming)
        {
            var existing = await _articles.Find(item => item.Id == article.Id).FirstOrDefaultAsync();
            if (existing is not null)
            {
                article.Embedding ??= existing.Embedding;
                article.FirstSeenAt = existing.FirstSeenAt;
                article.Content = existing.Content;
                article.Author = string.IsNullOrWhiteSpace(article.Author) ? existing.Author : article.Author;
                article.ImageUrl = string.IsNullOrWhiteSpace(article.ImageUrl) ? existing.ImageUrl : article.ImageUrl;
                article.ContentFetchedAt = existing.ContentFetchedAt;
                article.ContentFetchStatus = existing.ContentFetchStatus;
                article.ContentFetchError = existing.ContentFetchError;
            }

            article.UpdatedAt = DateTimeOffset.UtcNow;
            await _articles.ReplaceOneAsync(
                item => item.Id == article.Id,
                article,
                new ReplaceOptions { IsUpsert = true });
        }

        return incoming.ToList();
    }

    /// <summary>MongoDB 컬렉션에 필요한 조회/정렬용 인덱스를 최초 1회 생성합니다.</summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            await _articles.Indexes.CreateManyAsync([
                new CreateIndexModel<Article>(Builders<Article>.IndexKeys.Ascending(article => article.Id)),
                new CreateIndexModel<Article>(Builders<Article>.IndexKeys.Ascending(article => article.Url)),
                new CreateIndexModel<Article>(Builders<Article>.IndexKeys.Descending(article => article.PublishedAt)),
                new CreateIndexModel<Article>(Builders<Article>.IndexKeys.Ascending(article => article.Source)),
                new CreateIndexModel<Article>(Builders<Article>.IndexKeys.Ascending(article => article.ContentFetchStatus))
            ]);

            await _groups.Indexes.CreateManyAsync([
                new CreateIndexModel<ArticleGroup>(Builders<ArticleGroup>.IndexKeys.Ascending(group => group.Category)),
                new CreateIndexModel<ArticleGroup>(Builders<ArticleGroup>.IndexKeys.Descending(group => group.LatestPublishedAt))
            ]);

            await EnsureSummaryDateIndexAsync();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>요약 날짜 인덱스를 생성하되 기존 인덱스/중복 데이터 문제로 API 전체 오류가 나지 않게 처리합니다.</summary>
    private async Task EnsureSummaryDateIndexAsync()
    {
        var indexKeys = Builders<DailyIssueSummary>.IndexKeys.Ascending(summary => summary.Date);

        try
        {
            await _summaries.Indexes.CreateOneAsync(
                new CreateIndexModel<DailyIssueSummary>(
                    indexKeys,
                    new CreateIndexOptions { Unique = true, Name = "date_unique" }));
        }
        catch (MongoCommandException error) when (error.CodeName is "DuplicateKey" or "IndexOptionsConflict")
        {
            Console.WriteLine($"[mongo] unique summary date index skipped: {error.CodeName} {error.Message}");
        }
    }

    /// <summary>동일 날짜/기간 요약이 여러 건 있으면 최신 문서만 남겨 이후 조회와 저장이 안정적으로 동작하게 합니다.</summary>
    private async Task RemoveDuplicateSummariesAsync(string date)
    {
        var duplicates = await _summaries.Find(summary => summary.Date == date)
            .SortByDescending(summary => summary.GeneratedAt)
            .Skip(1)
            .ToListAsync();

        foreach (var duplicate in duplicates)
        {
            await _summaries.DeleteOneAsync(summary =>
                summary.Date == duplicate.Date &&
                summary.GeneratedAt == duplicate.GeneratedAt);
        }
    }
}
