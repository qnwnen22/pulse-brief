using MongoDB.Bson;
using MongoDB.Driver;

namespace PulseBrief.Tests;

internal static class MongoRecentArticlesTests
{
    public static async Task RunAsync()
    {
        if (Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING") is not null ||
            Environment.GetEnvironmentVariable("MONGO_DATABASE_NAME") is not null)
        {
            throw new InvalidOperationException("Unset MongoDB environment overrides before running this fixture.");
        }

        const string connection = "mongodb://127.0.0.1:37127/?directConnection=true&serverSelectionTimeoutMS=3000";
        const string indexName = "PublishedAt_-1_FirstSeenAt_-1";
        var expectedOrder = new BsonDocument { { "PublishedAt", -1 }, { "FirstSeenAt", -1 } };
        var name = $"pulsebrief_recent_test_{Guid.NewGuid():N}";
        var client = new MongoClient(connection);
        var database = client.GetDatabase(name);
        var articles = database.GetCollection<Article>("articles");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = connection,
            ["Mongo:DatabaseName"] = name
        }).Build();

        var publishedAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        try
        {
            await articles.InsertManyAsync(new[]
            {
                new Article { Id = "same-publication-earlier-seen", PublishedAt = publishedAt, FirstSeenAt = publishedAt.AddMinutes(1) },
                new Article { Id = "older-publication", PublishedAt = publishedAt.AddDays(-1), FirstSeenAt = publishedAt.AddMinutes(5) },
                new Article { Id = "same-publication-later-seen", PublishedAt = publishedAt, FirstSeenAt = publishedAt.AddMinutes(2) }
            });
            var store = new MongoArticleStore(configuration);
            var recent = await store.ReadRecentArticlesAsync(2);
            Check(recent.Select(article => article.Id).SequenceEqual(new[]
            {
                "same-publication-later-seen", "same-publication-earlier-seen"
            }), "The recent article limit or date tie-break order changed.");
            Check((await store.ReadRecentArticlesAsync(0)).Count == 1, "The minimum article limit changed.");

            using var cursor = await articles.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            Check(indexes.Any(index => index["name"].AsString == indexName && index["key"].Equals(expectedOrder)),
                "The recent article query is not backed by the expected compound index.");
            Console.WriteLine("PASS: MongoDB recent article limit, date ordering, tie-break and compound index.");
        }
        finally
        {
            await client.DropDatabaseAsync(name);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
