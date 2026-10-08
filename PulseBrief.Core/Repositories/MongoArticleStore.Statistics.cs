using MongoDB.Bson;
using MongoDB.Driver;

namespace PulseBrief;

public sealed partial class MongoArticleStore
{
    private const string CollectionIndex = "FirstSeenAt_statistics_1";
    private const int StatisticsPageSize = 250;
    private readonly IMongoCollection<CollectionDayStatistics> _collectionDays;
    private readonly IMongoCollection<CollectionStatisticsState> _collectionState;

    public Task<CollectionStatisticsState?> ReadCollectionStatisticsStateAsync(CancellationToken cancellationToken) =>
        _collectionState.Find(new BsonDocument("_id", "public"), new FindOptions { MaxTime = TimeSpan.FromSeconds(3) })
            .FirstOrDefaultAsync(cancellationToken)!;

    public Task<List<CollectionDayStatistics>> ReadCollectionDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (to.DayNumber - from.DayNumber is < 0 or > CollectionStatisticsService.MaxHistoryDays)
            throw new ArgumentOutOfRangeException(nameof(from));
        var filter = Builders<CollectionDayStatistics>.Filter.Gte(day => day.Id, KoreaDate.Key(from))
            & Builders<CollectionDayStatistics>.Filter.Lte(day => day.Id, KoreaDate.Key(to));
        return _collectionDays.Find(filter, new FindOptions { MaxTime = TimeSpan.FromSeconds(3) })
            .SortBy(day => day.Id).Limit(CollectionStatisticsService.MaxHistoryDays + 1).ToListAsync(cancellationToken);
    }

    // Provisioning is explicit: a public read or collector startup never builds an article index.
    public async Task InitializeCollectionStatisticsAsync(CancellationToken cancellationToken)
    {
        await _articles.Database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "createIndexes", "articles" },
            { "indexes", new BsonArray { new BsonDocument
                {
                    { "key", new BsonDocument { { "FirstSeenAt.DateTime", 1 }, { "_id", 1 }, { "FeedUrl", 1 }, { "Source", 1 } } },
                    { "name", CollectionIndex }
                } } },
            { "maxTimeMS", 120000 }
        }, cancellationToken: cancellationToken);
        var options = new FindOptions<Article, BsonDocument>
        {
            Hint = CollectionIndex, Limit = 1, MaxTime = TimeSpan.FromSeconds(5),
            Sort = new BsonDocument("FirstSeenAt.DateTime", 1),
            Projection = new BsonDocument { { "_id", 0 }, { "FirstSeenAt.DateTime", 1 } }
        };
        using var cursor = await _articles.FindAsync(new BsonDocument("FirstSeenAt.DateTime", new BsonDocument("$type", "date")), options, cancellationToken);
        var first = await cursor.FirstOrDefaultAsync(cancellationToken);
        if (first is null) return;
        var utc = first["FirstSeenAt"]["DateTime"].ToUniversalTime();
        var date = DateOnly.FromDateTime(utc.AddHours(9));
        if (KoreaDate.Today().DayNumber - date.DayNumber > CollectionStatisticsService.MaxHistoryDays)
            throw new InvalidOperationException("Collection statistics history exceeds the supported 10-year window.");
        await _collectionState.ReplaceOneAsync(state => state.Id == "public",
            new CollectionStatisticsState { FirstDate = KoreaDate.Key(date) }, new ReplaceOptions { IsUpsert = true }, cancellationToken);
    }

    public async Task RefreshCollectionDayAsync(DateOnly date, DateOnly today, CancellationToken cancellationToken)
    {
        if (date > today) throw new ArgumentOutOfRangeException(nameof(date));
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var start = KoreaDate.StartOfDay(date).UtcDateTime;
        var end = KoreaDate.StartOfDay(date.AddDays(1)).UtcDateTime;
        var publishers = new Dictionary<string, long>(StringComparer.Ordinal);
        var categories = new Dictionary<string, long>(StringComparer.Ordinal);
        DateTime? afterTime = null;
        string? afterId = null;
        long count = 0;
        while (true)
        {
            var filter = new BsonDocument("FirstSeenAt.DateTime", new BsonDocument { { "$gte", afterTime ?? start }, { "$lt", end } });
            if (afterTime is not null)
                filter = new BsonDocument("$and", new BsonArray { filter, new BsonDocument("$or", new BsonArray
                {
                    new BsonDocument("FirstSeenAt.DateTime", new BsonDocument("$gt", afterTime.Value)),
                    new BsonDocument { { "FirstSeenAt.DateTime", afterTime.Value }, { "_id", new BsonDocument("$gt", afterId) } }
                }) });
            var options = new FindOptions<Article, BsonDocument>
            {
                Hint = CollectionIndex, Limit = StatisticsPageSize, BatchSize = StatisticsPageSize, MaxTime = TimeSpan.FromSeconds(5),
                Sort = new BsonDocument { { "FirstSeenAt.DateTime", 1 }, { "_id", 1 } },
                Projection = new BsonDocument
                {
                    { "_id", 1 }, { "FirstSeenAt.DateTime", 1 }, { "FeedUrl", 1 }, { "Source", 1 },
                    { "Title", BoundedStatisticsText("Title", ArticleCategoryClassifier.MetadataTitleLength) },
                    { "Summary", BoundedStatisticsText("Summary", ArticleCategoryClassifier.MetadataSummaryLength) }
                }
            };
            using var cursor = await _articles.FindAsync(filter, options, budget.Token);
            var page = await cursor.ToListAsync(budget.Token);
            foreach (var row in page)
            {
                if (++count > 100000) throw new InvalidOperationException("Daily statistics row budget exceeded; incomplete results were not saved.");
                var feedValue = row.GetValue("FeedUrl", "");
                var sourceValue = row.GetValue("Source", "알 수 없음");
                var feed = feedValue.IsString ? feedValue.AsString : "";
                var source = sourceValue.IsString ? sourceValue.AsString : "알 수 없음";
                var publisher = string.IsNullOrWhiteSpace(feed) ? source : RssSourceCatalog.SourceInfoForUrl(feed).Publisher;
                if (string.IsNullOrWhiteSpace(publisher)) publisher = "알 수 없음";
                publishers[publisher] = publishers.GetValueOrDefault(publisher) + 1;
                var category = ArticleCategoryClassifier.ForMetadata(source, row["Title"].AsString, row["Summary"].AsString);
                categories[category] = categories.GetValueOrDefault(category) + 1;
            }
            if (page.Count < StatisticsPageSize) break;
            afterTime = page[^1]["FirstSeenAt"]["DateTime"].ToUniversalTime();
            afterId = page[^1]["_id"].AsString;
        }
        var day = new CollectionDayStatistics
        {
            Id = KoreaDate.Key(date), Publishers = publishers, Categories = categories,
            CategoryVersion = ArticleCategoryClassifier.StatisticsVersion, ArticleCount = count, IsComplete = date < today
        };
        await _collectionDays.ReplaceOneAsync(item => item.Id == day.Id, day, new ReplaceOptions { IsUpsert = true }, budget.Token);
    }

    private static BsonDocument BoundedStatisticsText(string field, int limit) => new("$substrCP", new BsonArray
    {
        new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", "$" + field), "string" }), "$" + field, ""
        }),
        0, limit
    });
}
