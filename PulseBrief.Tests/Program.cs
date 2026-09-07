using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using PulseBrief;
using PulseBrief.Tests;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
var testRoot = Path.Combine(Path.GetTempPath(), "pulsebrief-contract-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(testRoot, "config"));
await File.WriteAllTextAsync(Path.Combine(testRoot, "config/rss-feeds.txt"), "https://www.yna.co.kr/rss/news.xml\n");
// These credentials belong only to the in-process fixture server.
Environment.SetEnvironmentVariable("PULSEBRIEF_ADMIN_TOKEN", "pulsebrief-fixture-token");
Environment.SetEnvironmentVariable("ADMIN_API_TOKEN", null);
var store = new FakeArticleStore();
await using var app = WebApplicationBootstrap.Build(new WebApplicationOptions
{
    Args = [], ContentRootPath = testRoot, WebRootPath = Path.Combine(root, "wwwroot"), EnvironmentName = "Testing"
}, builder =>
{
    builder.Logging.ClearProviders();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Summary:EnableGeneration"] = "false", ["Collector:AllowWebManualRefresh"] = "false",
        ["Security:AllowLoopbackAdmin"] = "false", ["Collector:EnableInWebHost"] = "false",
        ["PublicBriefs:MaxGroups"] = "1000"
    });
    builder.Services.RemoveAll<IArticleStore>();
    builder.Services.AddSingleton<IArticleStore>(store);
});
app.Urls.Add(args.Contains("--serve") ? "http://127.0.0.1:4187" : "http://127.0.0.1:0");
await app.StartAsync();
try
{
    if (args.Contains("--serve"))
    {
        Console.WriteLine("Fixture preview: http://127.0.0.1:4187 (in-memory data only)");
        await app.WaitForShutdownAsync();
        return;
    }

    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    using var admin = new HttpClient { BaseAddress = client.BaseAddress };
    admin.DefaultRequestHeaders.Add("X-Admin-Token", "pulsebrief-fixture-token");
    var checks = 0;
    void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }

    async Task<JsonNode?> Request(HttpClient http, string method, string url, int status, object? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == status, $"{method} {url}: expected {status}, got {(int)response.StatusCode}: {text}");
        return response.Content.Headers.ContentType?.MediaType == "application/json" ? JsonNode.Parse(text) : null;
    }

    var expectedRoutes = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "PulseBrief.Tests/Fixtures/routes.json")))!.AsArray()
        .Select(item => $"{item!["method"]} {item["url"]}").Order().ToArray();
    var actualRoutes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
        .Where(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is not null)
        .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(method => $"{method} /{endpoint.RoutePattern.RawText!.TrimStart('/')}"))
        .Order().ToArray();
    Check(expectedRoutes.SequenceEqual(actualRoutes), "The controller routes differ from the 0.1.21 API surface.");

    await Request(client, "GET", "/", 200);
    await Request(client, "GET", "/admin", 200);
    var health = await Request(client, "GET", "/api/health", 200);
    Check(health!["ok"]!.GetValue<bool>() && health["hasOpenAiKey"] is null, "Public health fields changed.");
    var stats = await Request(client, "GET", "/api/news-stats", 200);
    Check(stats!["todayArticleCount"]!.GetValue<int>() == 1, "News stats contract changed.");
    var briefs = await Request(client, "GET", "/api/briefs", 200);
    Check(briefs!.AsArray().Count == 1 && briefs[0]!["relatedLinks"]!.AsArray().Count == 1, "Brief mapping changed.");
    foreach (var url in new[] { "/api/daily-summary", "/api/weekly-summary" })
    {
        var summary = await Request(client, "GET", url, 200);
        Check(summary!["provider"]!.GetValue<string>() == "manual", "Saved summary provider changed.");
        Check(summary["topIssues"]![0]!["relatedLinks"]!.AsArray().Count == 1, "Archived article link missing.");
    }
    Check(store.FullReads == 0 && store.SummaryWrites == 0, "Public reads must not scan all data or generate summaries.");

    foreach (var url in new[] { "/api/articles", "/api/groups", "/api/admin/dashboard", "/api/admin/diagnostics", "/api/admin/articles", "/api/admin/articles/test-article", "/api/admin/rss-feeds", "/api/daily-summary?force=true", "/api/weekly-summary?endDate=2026-09-06" })
        await Request(client, "GET", url, 401);
    foreach (var url in new[] { "/api/refresh", "/api/admin/refresh", "/api/admin/fetch-missing-content", "/api/admin/fetch-missing-images", "/api/admin/logout", "/api/admin/summaries/daily/regenerate", "/api/admin/summaries/daily/preview", "/api/admin/summaries/weekly/regenerate", "/api/admin/rss-feeds", "/api/admin/rss-feeds/remove" })
        await Request(client, "POST", url, 401, new { });
    await Request(client, "PATCH", "/api/admin/articles/test-article", 401, new { });
    await Request(client, "PATCH", "/api/admin/groups/test-group", 401, new { });
    await Request(client, "PATCH", "/api/admin/rss-feeds", 401, new { });
    Check(store.FullReads == 0, "Unauthorized requests reached the repository.");

    await Request(admin, "GET", "/api/daily-summary?date=invalid", 400);
    await Request(admin, "GET", "/api/weekly-summary?endDate=invalid", 400);
    await Request(admin, "GET", "/api/daily-summary?force=true", 409);
    await Request(admin, "GET", "/api/weekly-summary?force=true", 409);
    await Request(admin, "GET", "/api/daily-summary?date=2000-01-01", 404);
    foreach (var url in new[] { "/api/admin/summaries/daily/regenerate", "/api/admin/summaries/daily/preview", "/api/admin/summaries/weekly/regenerate", "/api/refresh", "/api/admin/refresh" })
        await Request(admin, "POST", url, 409, new { });
    Check(store.SummaryWrites == 0, "Disabled summary generation wrote data.");

    var articleResult = await Request(admin, "GET", "/api/admin/articles?query=테스트&pageSize=25", 200);
    Check(articleResult!["totalCount"]!.GetValue<int>() == 1, "Article query binding changed.");
    await Request(admin, "GET", "/api/admin/articles/missing", 404);
    var updated = await Request(admin, "PATCH", "/api/admin/articles/test-article", 200, new { title = "수정된 테스트 뉴스", category = "사회" });
    Check(updated!["title"]!.GetValue<string>() == "수정된 테스트 뉴스", "Article patch contract changed.");
    await Request(admin, "PATCH", "/api/admin/groups/test-group", 200, new { category = "경제/산업" });
    await Request(admin, "PATCH", "/api/admin/groups/missing", 404, new { category = "사회" });

    const string feed = "https://example.com/test-feed.xml";
    await Request(admin, "POST", "/api/admin/rss-feeds", 400, new { url = "not-a-url" });
    await Request(admin, "POST", "/api/admin/rss-feeds", 200, new { url = feed, isActive = true });
    await Request(admin, "POST", "/api/admin/rss-feeds", 409, new { url = feed, isActive = true });
    await Request(admin, "PATCH", "/api/admin/rss-feeds", 200, new { url = feed, isActive = false });
    await Request(admin, "POST", "/api/admin/rss-feeds/remove", 200, new { url = feed });
    await Request(admin, "POST", "/api/admin/rss-feeds/remove", 404, new { url = feed });
    await Request(admin, "GET", "/api/admin/diagnostics", 200);
    await Request(admin, "GET", "/api/admin/dashboard", 200);

    using var cookieClient = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = client.BaseAddress };
    await Request(cookieClient, "POST", "/api/admin/login", 401, new { token = "wrong" });
    var login = await Request(cookieClient, "POST", "/api/admin/login", 200, new { token = "pulsebrief-fixture-token" });
    var session = await Request(cookieClient, "GET", "/api/admin/session", 200);
    Check(session!["authenticated"]!.GetValue<bool>(), "Session cookie authentication failed.");
    await Request(cookieClient, "PATCH", "/api/admin/articles/test-article", 403, new { title = "blocked" });
    cookieClient.DefaultRequestHeaders.Add("X-CSRF-Token", login!["csrfToken"]!.GetValue<string>());
    await Request(cookieClient, "PATCH", "/api/admin/articles/test-article", 200, new { title = "CSRF checked" });
    await Request(cookieClient, "POST", "/api/admin/logout", 200);

    var document = store.Summaries.Values.First().ToBsonDocument();
    Check(document.Contains("Date") && !document["TopIssues"][0].AsBsonDocument.Contains("RelatedLinks"), "Stored BSON contract changed.");
    Check(BsonSerializer.Deserialize<DailyIssueSummary>(document).Provider == "manual", "Summary BSON roundtrip failed.");
    var linksService = app.Services.GetRequiredService<SummaryLinkService>();
    await linksService.AddLinksAsync(new DailyIssueSummary { TopIssues = Enumerable.Range(0, 20).Select(index => new DailyTopIssue { ArticleIds = Enumerable.Range(0, 150).Select(id => $"{index}-{id}").ToArray() }).ToArray() });
    Check(store.LargestIdLookup <= 1000, "Summary link lookup exceeded its bound.");
    store.FailLinkLookup = true;
    await Request(client, "GET", "/api/daily-summary", 200);
    Console.WriteLine($"PASS: {checks} API, authentication, CSRF, repository-bound and serialization checks; no production DB or OpenAI calls.");
}
finally
{
    await app.StopAsync();
    if (testRoot.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(testRoot, true);
}
