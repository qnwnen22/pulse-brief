using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
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
    builder.Services.RemoveAll<ICollectionStatisticsStore>();
    builder.Services.AddSingleton<ICollectionStatisticsStore>(new FakeCollectionStatisticsStore());
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

    async Task AssertThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
    {
        try { await action(); }
        catch (TException) { checks++; return; }
        throw new InvalidOperationException(message);
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
    await Request(client, "POST", "/mcp", 404, new { jsonrpc = "2.0", id = 1, method = "initialize" });
    var health = await Request(client, "GET", "/api/health", 200);
    Check(health!["ok"]!.GetValue<bool>() && health["hasOpenAiKey"] is null, "Public health fields changed.");
    var stats = await Request(client, "GET", "/api/news-stats", 200);
    Check(stats!["todayArticleCount"]!.GetValue<int>() == 1, "News stats contract changed.");
    foreach (var period in new[] { "7", "30", "all" })
    {
        var collectionStats = await Request(client, "GET", $"/api/collection-statistics?period={period}", 200);
        Check(collectionStats!["isReady"]!.GetValue<bool>(), "Completed statistics cache was not ready.");
        Check(collectionStats["todayCount"]!.GetValue<int>() == 5, "Today's provisional collection count changed.");
    }
    await Request(client, "GET", "/api/collection-statistics?period=invalid", 400);
    await Request(client, "GET", "/api/collection-statistics?period=999999", 400);
    var collectionStore = (FakeCollectionStatisticsStore)app.Services.GetRequiredService<ICollectionStatisticsStore>();
    collectionStore.FailReads = true;
    var failedStats = await Request(client, "GET", "/api/collection-statistics", 503);
    Check(failedStats!["message"]!.GetValue<string>() != "Fixture cache unavailable", "Statistics errors leaked internal exception details.");
    collectionStore.FailReads = false;
    await CollectionStatisticsTests.RunAsync(Check);
    var briefs = await Request(client, "GET", "/api/briefs", 200);
    Check(briefs!.AsArray().Count == 1 && briefs[0]!["relatedLinks"]!.AsArray().Count == 1, "Brief mapping changed.");
    foreach (var url in new[] { "/api/daily-summary", "/api/weekly-summary" })
    {
        var summary = await Request(client, "GET", url, 200);
        Check(summary!["provider"]!.GetValue<string>() == "manual", "Saved summary provider changed.");
        Check(summary["topIssues"]![0]!["relatedLinks"]!.AsArray().Count == 1, "Archived article link missing.");
    }
    var dailySummaryDates = await Request(client, "GET", "/api/daily-summary/dates", 200);
    Check(dailySummaryDates!.AsArray().Count == 2, "Daily summary history must include daily dates and exclude weekly summary keys.");
    var storedDailyDate = dailySummaryDates[0]!.GetValue<string>();
    Check(DateOnly.TryParseExact(storedDailyDate, "yyyy-MM-dd", out _), "Daily summary history returned an invalid date key.");
    Check(string.CompareOrdinal(storedDailyDate, dailySummaryDates[1]!.GetValue<string>()) > 0, "Daily summary history is not sorted newest first.");
    await Request(client, "GET", $"/api/daily-summary?date={storedDailyDate}", 200);
    await Request(client, "GET", "/api/daily-summary?date=invalid", 400);
    await Request(client, "GET", "/api/daily-summary?date=2000-01-01", 404);
    Check(store.FullReads == 0 && store.SummaryWrites == 0, "Public reads must not scan all data or generate summaries.");
    var pipelineConstructor = typeof(NewsPipeline).GetConstructors().Single();
    Check(!pipelineConstructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(DailySummaryService)), "News collection pipeline must not depend on summary generation.");
    Check(typeof(DailySummaryService).GetMethod("EnsureScheduledSummariesAsync") is null, "Scheduled local summary generation must not be available.");
    var boundaryText = new string('가', 699) + "😀끝";
    var safelyTruncated = TextCleaner.Truncate(boundaryText, 700);
    var strictUtf8 = new UTF8Encoding(false, true);
    Check(safelyTruncated.Length == 699, "Unicode truncation split a surrogate pair.");
    Check(strictUtf8.GetString(strictUtf8.GetBytes(safelyTruncated)) == safelyTruncated, "Truncated text is not valid UTF-8.");
    Check(TextCleaner.Clean("정상\uD83D문자") == "정상\uFFFD문자", "Malformed Unicode was not normalized.");
    var unicodeGroup = app.Services.GetRequiredService<ArticleClusterer>().GroupSimilarArticles([
        new Article { Id = "unicode-boundary", Title = "유니코드 경계 테스트", Source = "테스트", Content = boundaryText, Embedding = [1d] }
    ]).Single();
    Check(unicodeGroup.SeedSummary == safelyTruncated, "Article grouping did not use safe Unicode truncation.");
    Check(unicodeGroup.ToBson().Length > 0, "Unicode-safe article group could not be serialized to BSON.");

    foreach (var url in new[] { "/api/articles", "/api/groups", "/api/admin/dashboard", "/api/admin/diagnostics", "/api/admin/articles", "/api/admin/articles/test-article", "/api/admin/rss-feeds", "/api/daily-summary?force=true", "/api/weekly-summary?endDate=2026-09-06" })
        await Request(client, "GET", url, 401);
    foreach (var url in new[] { "/api/refresh", "/api/admin/refresh", "/api/admin/fetch-missing-content", "/api/admin/fetch-missing-images", "/api/admin/logout", "/api/admin/summaries/daily/regenerate", "/api/admin/summaries/daily/preview", "/api/admin/summaries/weekly/regenerate", "/api/admin/rss-feeds", "/api/admin/rss-feeds/remove" })
        await Request(client, "POST", url, 401, new { });
    await Request(client, "PATCH", "/api/admin/articles/test-article", 401, new { });
    await Request(client, "PATCH", "/api/admin/groups/test-group", 401, new { });
    await Request(client, "PATCH", "/api/admin/rss-feeds", 401, new { });
    Check(store.FullReads == 0, "Unauthorized requests reached the repository.");

    await Request(admin, "GET", "/api/weekly-summary?endDate=invalid", 400);
    await Request(admin, "GET", "/api/daily-summary?force=true", 409);
    await Request(admin, "GET", "/api/weekly-summary?force=true", 409);
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
    store.FailLinkLookup = false;

    var disabledMcpService = app.Services.GetRequiredService<ManualSummaryMcpService>();
    var mcpDate = KoreaDate.Today().AddDays(-3);
    await AssertThrowsAsync<InvalidOperationException>(() => disabledMcpService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate)),
        "Manual summary export must remain disabled by default.");
    Check(store.ManualArticleRangeReads == 0, "Disabled MCP export reached the article repository.");
    var mcpConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Mcp:DailySummary:Enabled"] = "true"
    }).Build();
    var mcpService = new ManualSummaryMcpService(store, mcpConfig);
    var mcpStart = KoreaDate.StartOfDay(mcpDate);
    await AssertThrowsAsync<ArgumentOutOfRangeException>(() => mcpService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate), 10001),
        "MCP export must enforce its article ceiling.");
    store.Articles.AddRange([
        new Article { Id = "mcp-midnight", Title = "At midnight", Url = "https://example.com/midnight", Source = "Example", PublishedAt = mcpStart },
        new Article { Id = "mcp-in-period", Title = "MCP evidence", Url = "https://example.com/mcp", Source = "Example", PublishedAt = mcpStart.AddHours(2) },
        new Article { Id = "mcp-excluded", Title = "Excluded", PublishedAt = mcpStart.AddHours(3), IsExcluded = true },
        new Article { Id = "mcp-next-day", Title = "Next day", PublishedAt = mcpStart.AddDays(1) }
    ]);
    var exported = await mcpService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate), maxArticles: 10);
    Check(exported.Status == "exported" && exported.Complete && exported.ArticleCount == 2
        && exported.Articles.Select(article => article.Id).ToHashSet().SetEquals(["mcp-midnight", "mcp-in-period"])
        && exported.Sha256?.Length == 64,
        "MCP export did not enforce the date range and exclusion filter.");
    var pagedStore = new FakeArticleStore();
    for (var index = 0; index < 102; index++)
    {
        pagedStore.Articles.Add(new Article
        {
            Id = $"page-{index:D3}", Title = $"Page article {index}", Url = $"https://example.test/page-{index}",
            Source = "Example", Content = new string('A', 100), PublishedAt = mcpStart.AddMinutes(index),
            FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
    }
    var pagedService = new ManualSummaryMcpService(pagedStore, mcpConfig);
    var firstPage = await pagedService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate), maxArticles: 200);
    Check(!firstPage.Complete && firstPage.ArticleCount == 100 && firstPage.TotalArticleCount == 102
        && firstPage.ExportedArticleCount == 100 && !string.IsNullOrWhiteSpace(firstPage.NextCursor)
        && firstPage.Sha256?.Length == 64,
        "MCP first page did not report a bounded page and stable snapshot totals.");
    pagedStore.Articles.Add(new Article
    {
        Id = "late-arrival", Title = "Late arrival", Url = "https://example.test/late", Source = "Example",
        Content = "late", PublishedAt = mcpStart.AddMinutes(30), FirstSeenAt = DateTimeOffset.UtcNow.AddHours(1)
    });
    await AssertThrowsAsync<ArgumentException>(
        () => pagedService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate), maxArticles: 201, cursor: firstPage.NextCursor),
        "An export cursor must remain bound to the original max-article ceiling.");
    var finalPage = await pagedService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate), maxArticles: 200, cursor: firstPage.NextCursor);
    var allPageIds = firstPage.Articles.Concat(finalPage.Articles).Select(article => article.Id).ToArray();
    Check(finalPage.Complete && finalPage.ArticleCount == 2 && finalPage.TotalArticleCount == 102
        && finalPage.ExportedArticleCount == 102 && finalPage.NextCursor is null && finalPage.Sha256?.Length == 64
        && allPageIds.Length == 102 && allPageIds.Distinct(StringComparer.Ordinal).Count() == 102
        && !allPageIds.Contains("late-arrival", StringComparer.Ordinal),
        "MCP continuation duplicated, skipped, or included rows outside its FirstSeenAt snapshot.");
    var bytePagedStore = new FakeArticleStore();
    bytePagedStore.Articles.AddRange([
        new Article { Id = "byte-page-1", Title = "Large one", Url = "https://example.test/large-1", Source = "Example", Content = new string('한', 100_000), PublishedAt = mcpStart.AddHours(2), FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
        new Article { Id = "byte-page-2", Title = "Large two", Url = "https://example.test/large-2", Source = "Example", Content = new string('한', 100_000), PublishedAt = mcpStart.AddHours(1), FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5) }
    ]);
    var bytePagedService = new ManualSummaryMcpService(bytePagedStore, mcpConfig);
    var byteFirst = await bytePagedService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate), maxArticles: 10);
    var byteFinal = await bytePagedService.ExportDailyArticlesAsync(KoreaDate.Key(mcpDate), maxArticles: 10, cursor: byteFirst.NextCursor);
    Check(!byteFirst.Complete && byteFirst.ArticleCount == 1 && byteFirst.NextCursor is not null
        && byteFinal.Complete && byteFinal.ArticleCount == 1 && byteFinal.ExportedArticleCount == 2,
        "MCP byte budget did not split a page without dropping or truncating an article.");
    var oversizedDate = mcpDate.AddDays(-1);
    var oversizedStore = new FakeArticleStore();
    var oversizedStart = KoreaDate.StartOfDay(oversizedDate);
    oversizedStore.Articles.AddRange([
        new Article { Id = "oversized-first", Title = "Oversized", Url = "https://example.test/large", Source = "Example", Content = new string('가', (2 * 1024 * 1024 / 6) + 1), PublishedAt = oversizedStart.AddHours(2) },
        new Article { Id = "later-row", Title = "Later", Url = "https://example.test/later", Source = "Example", Content = "small", PublishedAt = oversizedStart.AddHours(1) }
    ]);
    var oversizedService = new ManualSummaryMcpService(oversizedStore, mcpConfig);
    await AssertThrowsAsync<InvalidOperationException>(() => oversizedService.ExportDailyArticlesAsync(KoreaDate.Key(oversizedDate)),
        "MCP export must fail closed when a single article exceeds the output budget.");
    Check(oversizedStore.ManualArticleRowsYielded == 1,
        "MCP export continued consuming rows after exceeding its bounded payload budget.");
    var candidate = new DailyIssueSummary
    {
        Date = KoreaDate.Key(mcpDate), GeneratedAt = DateTimeOffset.UtcNow, Provider = "manual", Model = "Codex",
        Headline = "MCP headline", Summary = "Daily summary", ArticleCount = 2, IssueCount = 1, SourceCount = 1,
        Categories = [new DailyCategorySummary { Category = "사회", IssueCount = 1, ArticleCount = 2, Summary = "Category summary" }],
        TopIssues = [new DailyTopIssue { Title = "MCP issue", Category = "사회", Summary = "Issue summary", ArticleCount = 1, ArticleIds = ["mcp-in-period"], Score = 50, Sources = ["Example"], Keywords = ["news"] }]
    };
    var published = await mcpService.PublishDailySummaryAsync(candidate.Date, candidate);
    Check(published.Status == "published" && published.Matches && store.ManualSummaryInserts == 1,
        "MCP publication did not insert and verify the valid summary.");
    var conflictingCandidate = new DailyIssueSummary
    {
        Date = candidate.Date, GeneratedAt = candidate.GeneratedAt, Provider = candidate.Provider, Model = candidate.Model,
        Headline = "Different headline", Summary = candidate.Summary, ArticleCount = 2, IssueCount = 1, SourceCount = 1,
        Categories = candidate.Categories, TopIssues = candidate.TopIssues
    };
    var existingPublication = await mcpService.PublishDailySummaryAsync(candidate.Date, conflictingCandidate);
    Check(existingPublication.Status == "existing" && !existingPublication.Matches && store.ManualSummaryInserts == 1
        && store.Summaries[candidate.Date].Headline == candidate.Headline,
        "MCP publication overwrote an existing date.");
    var readsBeforeExistingExport = store.ManualArticleRangeReads;
    var existingExport = await mcpService.ExportDailyArticlesAsync(candidate.Date);
    Check(existingExport.Status == "existing" && existingExport.Articles.Count == 0
        && store.ManualArticleRangeReads == readsBeforeExistingExport,
        "MCP export read article data after finding an existing summary.");
    var badEvidence = new DailyIssueSummary
    {
        Date = KoreaDate.Key(mcpDate), GeneratedAt = DateTimeOffset.UtcNow, Provider = "manual", Model = "Codex",
        Headline = "Bad", Summary = "Bad", ArticleCount = 1, IssueCount = 1, SourceCount = 1,
        Categories = [new DailyCategorySummary { Category = "사회", IssueCount = 1, ArticleCount = 1, Summary = "Category" }],
        TopIssues = [new DailyTopIssue { Title = "Wrong day", Category = "사회", Summary = "Bad", ArticleCount = 1, ArticleIds = ["mcp-next-day"], Score = 50, Sources = ["Example"], Keywords = ["news"] }]
    };
    await AssertThrowsAsync<InvalidOperationException>(() => mcpService.PublishDailySummaryAsync(badEvidence.Date, badEvidence),
        "MCP publication must reject evidence outside the requested day.");
    var excludedEvidence = new DailyIssueSummary
    {
        Date = KoreaDate.Key(mcpDate), GeneratedAt = DateTimeOffset.UtcNow, Provider = "manual", Model = "Codex",
        Headline = "Excluded", Summary = "Excluded", ArticleCount = 1, IssueCount = 1, SourceCount = 1,
        Categories = [new DailyCategorySummary { Category = "사회", IssueCount = 1, ArticleCount = 1, Summary = "Category" }],
        TopIssues = [new DailyTopIssue { Title = "Excluded", Category = "사회", Summary = "Excluded", ArticleCount = 1, ArticleIds = ["mcp-excluded"], Score = 50, Sources = ["Example"], Keywords = ["news"] }]
    };
    await AssertThrowsAsync<InvalidOperationException>(() => mcpService.PublishDailySummaryAsync(excludedEvidence.Date, excludedEvidence),
        "MCP publication must reject excluded evidence.");
    Check(store.ManualSummaryInserts == 1, "Invalid MCP evidence wrote a summary.");
    var cloudflareConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Mcp:DailySummary:Enabled"] = "true",
        ["Mcp:CloudflareAccess:TeamDomain"] = "https://fixture.cloudflareaccess.com",
        ["Mcp:CloudflareAccess:Hostname"] = "mcp.fixture.test",
        ["Mcp:CloudflareAccess:Audience"] = "fixture-dedicated-aud",
        ["Mcp:CloudflareAccess:AllowedEmails:0"] = "operator@example.test"
    }).Build();
    using var rsa = RSA.Create(2048);
    var rsaKey = new RsaSecurityKey(rsa) { KeyId = "fixture-rsa" };
    var rsaParameters = rsa.ExportParameters(false);
    var jwks = JsonSerializer.Serialize(new
    {
        keys = new[] { new { kty = "RSA", use = "sig", kid = rsaKey.KeyId, alg = "RS256", n = Base64UrlEncoder.Encode(rsaParameters.Modulus!), e = Base64UrlEncoder.Encode(rsaParameters.Exponent!) } }
    });
    using var jwksClient = new HttpClient(new FixtureJwksHandler(jwks));
    var authenticator = new CloudflareAccessJwtAuthenticator(cloudflareConfig, jwksClient);
    string CreateAssertion(string email, string issuer, string audience, DateTime expires, RSA signingKey)
    {
        var claims = new[] { new Claim("email", email) };
        var notBefore = expires < DateTime.UtcNow ? expires.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore: notBefore, expires: expires,
            signingCredentials: new SigningCredentials(new RsaSecurityKey(signingKey) { KeyId = "fixture-rsa" }, "RS256"));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    async Task<bool> Authenticates(string email, string issuer, string audience, DateTime expires, RSA signingKey)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Cf-Access-Jwt-Assertion"] = CreateAssertion(email, issuer, audience, expires, signingKey);
        return await authenticator.AuthenticateAsync(context, CancellationToken.None) is not null;
    }
    Check(await Authenticates("operator@example.test", "https://fixture.cloudflareaccess.com", "fixture-dedicated-aud", DateTime.UtcNow.AddMinutes(5), rsa),
        "Valid Cloudflare-origin assertion was rejected.");
    Check(!await Authenticates("operator@example.test", "https://wrong.cloudflareaccess.com", "fixture-dedicated-aud", DateTime.UtcNow.AddMinutes(5), rsa),
        "JWT with wrong issuer was accepted.");
    Check(!await Authenticates("operator@example.test", "https://fixture.cloudflareaccess.com", "other-app-audience", DateTime.UtcNow.AddMinutes(5), rsa),
        "JWT for another Access application was accepted.");
    Check(!await Authenticates("other@example.test", "https://fixture.cloudflareaccess.com", "fixture-dedicated-aud", DateTime.UtcNow.AddMinutes(5), rsa),
        "JWT for an unapproved identity was accepted.");
    Check(!await Authenticates("operator@example.test", "https://fixture.cloudflareaccess.com", "fixture-dedicated-aud", DateTime.UtcNow.AddMinutes(-5), rsa),
        "Expired JWT was accepted.");
    using var forgedRsa = RSA.Create(2048);
    Check(!await Authenticates("operator@example.test", "https://fixture.cloudflareaccess.com", "fixture-dedicated-aud", DateTime.UtcNow.AddMinutes(5), forgedRsa),
        "JWT with an unknown signing key was accepted.");
    var missingHeaderContext = new DefaultHttpContext();
    Check(await authenticator.AuthenticateAsync(missingHeaderContext, CancellationToken.None) is null,
        "An assertion-free request used a fallback authentication method.");

    var enabledTestRoot = Path.Combine(Path.GetTempPath(), "pulsebrief-mcp-host-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(enabledTestRoot, "config"));
    await File.WriteAllTextAsync(Path.Combine(enabledTestRoot, "config/rss-feeds.txt"), "https://www.yna.co.kr/rss/news.xml\n");
    var mcpStore = new FakeArticleStore();
    mcpStore.Articles.Add(new Article
    {
        Id = "mcp-in-period", Title = "Route evidence", Url = "https://example.test/evidence", Source = "Example",
        PublishedAt = KoreaDate.StartOfDay(mcpDate).AddHours(2)
    });
    await using (var mcpApp = WebApplicationBootstrap.Build(new WebApplicationOptions
    {
        Args = [], ContentRootPath = enabledTestRoot, WebRootPath = Path.Combine(root, "wwwroot"), EnvironmentName = "Testing"
    }, builder =>
    {
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:DailySummary:Enabled"] = "true",
            ["Mcp:CloudflareAccess:TeamDomain"] = "https://fixture.cloudflareaccess.com",
            ["Mcp:CloudflareAccess:Hostname"] = "mcp.fixture.test",
            ["Mcp:CloudflareAccess:Audience"] = "fixture-dedicated-aud",
            ["Mcp:CloudflareAccess:AllowedEmails:0"] = "operator@example.test",
            ["Summary:EnableGeneration"] = "false", ["Collector:AllowWebManualRefresh"] = "false",
            ["Security:AllowLoopbackAdmin"] = "false", ["Collector:EnableInWebHost"] = "false"
        });
        builder.Services.AddHttpClient("cloudflare-access-jwks")
            .ConfigurePrimaryHttpMessageHandler(() => new FixtureJwksHandler(jwks));
        builder.Services.RemoveAll<IArticleStore>();
        builder.Services.AddSingleton<IArticleStore>(mcpStore);
    }))
    {
        mcpApp.Urls.Add("http://127.0.0.1:0");
        await mcpApp.StartAsync();
        try
        {
            using var mcpClient = new HttpClient { BaseAddress = new Uri(mcpApp.Urls.Single()) };
            async Task<HttpResponseMessage> HostRequest(string host, string method, string path, object? body = null, bool authenticated = false)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                request.Headers.Host = host;
                if (authenticated)
                    request.Headers.Add("Cf-Access-Jwt-Assertion", CreateAssertion("operator@example.test", "https://fixture.cloudflareaccess.com", "fixture-dedicated-aud", DateTime.UtcNow.AddMinutes(5), rsa));
                if (method == "POST") request.Content = JsonContent.Create(body ?? new { jsonrpc = "2.0", id = 1, method = "initialize" });
                return await mcpClient.SendAsync(request);
            }
            using (var response = await HostRequest("mcp.fixture.test", "GET", "/api/health")) Check((int)response.StatusCode == 404, "The dedicated MCP hostname exposed an existing API.");
            using (var response = await HostRequest("mcp.fixture.test", "GET", "/mcp")) Check((int)response.StatusCode == 405, "The MCP hostname allowed a non-POST transport request.");
            using (var response = await HostRequest("mcp.fixture.test", "POST", "/mcp")) Check((int)response.StatusCode == 401, "The enabled MCP route accepted a request without Cloudflare Access assertion.");
            using (var response = await HostRequest("news.fixture.test", "POST", "/mcp")) Check((int)response.StatusCode == 404, "MCP was reachable on a non-MCP hostname.");
            using (var response = await HostRequest("mcp.fixture.test", "POST", "/mcp", new { jsonrpc = "2.0", id = 2, method = "initialize", @params = new { protocolVersion = "2025-03-26" } }, authenticated: true))
            {
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                Check(response.StatusCode == HttpStatusCode.OK && json?["result"]?["protocolVersion"]?.GetValue<string>() == "2025-03-26",
                    "Authenticated MCP initialize failed.");
            }
            using (var response = await HostRequest("mcp.fixture.test", "POST", "/mcp", new { jsonrpc = "2.0", id = 3, method = "tools/list" }, authenticated: true))
            {
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                Check(json?["result"]?["tools"]?.AsArray().Count == 2, "MCP exposed tools outside the approved two-operation surface.");
            }
            var routeExportCall = new { jsonrpc = "2.0", id = 6, method = "tools/call", @params = new { name = "pulsebrief_export_daily_articles", arguments = new { date = KoreaDate.Key(mcpDate), maxArticles = 10 } } };
            using (var response = await HostRequest("mcp.fixture.test", "POST", "/mcp", routeExportCall, authenticated: true))
            {
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                var resultText = json?["result"]?["content"]?[0]?["text"]?.GetValue<string>();
                Check(json?["result"]?["isError"]?.GetValue<bool>() == false
                    && resultText?.Contains("\"articleCount\":1", StringComparison.Ordinal) == true
                    && resultText.Contains("\"complete\":true", StringComparison.Ordinal),
                    "Authenticated MCP export did not return the complete in-range article payload.");
            }
            var routeSummary = candidate;
            var publicationCall = new { jsonrpc = "2.0", id = 4, method = "tools/call", @params = new { name = "pulsebrief_publish_daily_summary", arguments = new { date = routeSummary.Date, summary = routeSummary } } };
            using (var response = await HostRequest("mcp.fixture.test", "POST", "/mcp", publicationCall, authenticated: true))
            {
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                Check(json?["result"]?["isError"]?.GetValue<bool>() == false
                    && json["result"]?["content"]?[0]?["text"]?.GetValue<string>()?.Contains("\"status\":\"published\"", StringComparison.Ordinal) == true,
                    "Authenticated MCP publication did not insert the validated summary.");
            }
            routeSummary.Headline = "Different repeat";
            var retryCall = new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "pulsebrief_publish_daily_summary", arguments = new { date = routeSummary.Date, summary = routeSummary } } };
            using (var response = await HostRequest("mcp.fixture.test", "POST", "/mcp", retryCall, authenticated: true))
            {
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                Check(json?["result"]?["content"]?[0]?["text"]?.GetValue<string>()?.Contains("\"status\":\"existing\"", StringComparison.Ordinal) == true
                    && mcpStore.ManualSummaryInserts == 1 && mcpStore.Summaries[routeSummary.Date].Headline == "MCP headline",
                    "MCP retry replaced an existing daily summary.");
            }
        }
        finally { await mcpApp.StopAsync(); }
    }
    Directory.Delete(enabledTestRoot, true);
    Console.WriteLine($"PASS: {checks} API, auth boundaries, CSRF, MCP export/publication, repository-bound and serialization checks; no production DB or OpenAI calls.");
}
finally
{
    await app.StopAsync();
    if (testRoot.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(testRoot, true);
}

sealed class FixtureJwksHandler(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
}
