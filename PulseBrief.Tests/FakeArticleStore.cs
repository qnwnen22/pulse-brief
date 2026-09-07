using PulseBrief;

namespace PulseBrief.Tests;

public sealed class FakeArticleStore : IArticleStore
{
    public List<Article> Articles { get; } = [new Article
    {
        Id = "test-article", Title = "테스트 뉴스", Source = "연합뉴스",
        Url = "https://example.com/article", FeedUrl = "https://www.yna.co.kr/rss/news.xml",
        Summary = "테스트 기사 요약", Content = "테스트 본문", ContentFetchStatus = "success",
        ImageUrl = "https://example.com/image.png", PublishedAt = DateTimeOffset.UtcNow
    }];
    public List<ArticleGroup> Groups { get; } = [new ArticleGroup
    {
        Id = "test-group", Category = "정치/정책", ArticleIds = ["test-article"], ArticleCount = 1,
        Sources = ["연합뉴스"], RepresentativeTitle = "테스트 뉴스", Summary = "테스트 이슈 요약"
    }];
    public Dictionary<string, DailyIssueSummary> Summaries { get; } = new();
    public int FullReads { get; private set; }
    public int SummaryWrites { get; private set; }
    public int LargestIdLookup { get; private set; }
    public bool FailLinkLookup { get; set; }

    public FakeArticleStore()
    {
        var today = KoreaDate.Today();
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var end = today.AddDays(-daysSinceMonday - 1);
        foreach (var key in new[] { today.AddDays(-1).ToString("yyyy-MM-dd"), $"weekly:{end.AddDays(-6):yyyy-MM-dd}:{end:yyyy-MM-dd}" })
        {
            Summaries[key] = new DailyIssueSummary
            {
                Date = key, Provider = "manual", Model = "Codex", Headline = "검증용 요약",
                Summary = "검증용 요약 본문", ArticleCount = 1, IssueCount = 1, SourceCount = 1,
                Categories = [new DailyCategorySummary { Category = "정치/정책", Summary = "저장된 정치 요약", ArticleCount = 1, IssueCount = 1 }],
                TopIssues = [new DailyTopIssue { Title = "테스트 이슈", Category = "정치/정책", Summary = "주요 이슈 본문", ArticleIds = ["test-article"], ArticleCount = 1, Sources = ["연합뉴스"], Score = 50 }]
            };
        }
    }

    public Task<List<Article>> ReadArticlesAsync() { FullReads++; return Task.FromResult(Articles); }
    public Task<List<ArticleGroup>> ReadGroupsAsync() { FullReads++; return Task.FromResult(Groups); }
    public Task<List<Article>> ReadRecentArticlesAsync(int limit) => Task.FromResult(Articles.Take(limit).ToList());
    public Task<List<ArticleGroup>> ReadRecentGroupsAsync(int limit) => Task.FromResult(Groups.Take(limit).ToList());
    public Task<List<Article>> ReadArticlesByIdsAsync(IReadOnlyCollection<string> ids)
    {
        LargestIdLookup = Math.Max(LargestIdLookup, ids.Count);
        if (FailLinkLookup) throw new InvalidOperationException("Simulated link lookup failure");
        return Task.FromResult(Articles.Where(article => ids.Contains(article.Id)).ToList());
    }
    public Task<NewsStats?> ReadNewsStatsAsync(CancellationToken cancellationToken = default) => Task.FromResult<NewsStats?>(new NewsStats { TodayDate = KoreaDate.Key(KoreaDate.Today()), TodayArticleCount = Articles.Count });
    public async Task<NewsStats> RefreshNewsStatsAsync(CancellationToken cancellationToken = default) => (await ReadNewsStatsAsync(cancellationToken))!;
    public Task<DailyIssueSummary?> ReadDailySummaryAsync(string date) => Task.FromResult(Summaries.GetValueOrDefault(date));
    public Task<List<DailyIssueSummary>> ReadDailySummariesAsync() => Task.FromResult(Summaries.Values.ToList());
    public Task SaveDailySummaryAsync(DailyIssueSummary summary) { SummaryWrites++; Summaries[summary.Date] = summary; return Task.CompletedTask; }
    public Task SaveArticlesAsync(IReadOnlyCollection<Article> articles) => Task.CompletedTask;
    public Task SaveGroupsAsync(IReadOnlyCollection<ArticleGroup> groups) => Task.CompletedTask;
    public Task<List<Article>> UpsertArticlesAsync(IReadOnlyCollection<Article> incoming) => Task.FromResult(Articles);
}
