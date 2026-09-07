namespace PulseBrief;

/// <summary>저장소의 도메인 모델을 프론트엔드 API 응답 DTO로 변환합니다.</summary>
public static class ApiMapper
{
    /// <summary>이슈 그룹과 기사 목록을 이슈 피드에서 사용하는 브리프 DTO 목록으로 변환합니다.</summary>
    public static IEnumerable<BriefDto> ToBriefs(IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var byId = articles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);

        return groups.Select(group =>
        {
            var groupArticles = ArticleDedupe.EffectiveArticles(group.ArticleIds.Select(id => byId.GetValueOrDefault(id)));
            if (groupArticles.Length == 0) return null;

            var latest = groupArticles.FirstOrDefault();
            var minutes = latest is null
                ? 1
                : Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - latest.PublishedAt).TotalMinutes));
            var title = TextCleaner.Clean(group.RepresentativeTitle);
            var sources = groupArticles
                .Select(article => TextCleaner.Clean(article.Source))
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sources.Length == 0)
            {
                sources = group.Sources
                    .Select(TextCleaner.Clean)
                    .Where(source => !string.IsNullOrWhiteSpace(source))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            var sourceCount = sources.Length;
            var articleCount = groupArticles.Length;
            var publishers = groupArticles
                .Select(article => RssSourceCatalog.SourceInfoForUrl(article.FeedUrl).Publisher)
                .Where(publisher => !string.IsNullOrWhiteSpace(publisher))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var impact = articleCount > 0
                ? IssueSignalCalculator.CalculateImpact(articleCount, sourceCount, title, sources)
                : IssueSignalCalculator.CalculateImpact(group.ArticleCount, sourceCount, title, sources);

            return new BriefDto
            {
                Title = title,
                Category = group.Category,
                Source = TextCleaner.Clean(string.Join(", ", sources.DefaultIfEmpty().Take(2))),
                Publishers = publishers,
                Minutes = minutes,
                Impact = impact,
                Heat = IssueSignalCalculator.HeatFromImpact(impact),
                Summary = BuildSafeSummary(group, groupArticles, articleCount, sourceCount),
                ImageUrl = groupArticles.Select(article => article.ImageUrl).FirstOrDefault(imageUrl => !string.IsNullOrWhiteSpace(imageUrl)) ?? "",
                Keywords = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length >= 2).Take(4).ToArray(),
                ArticleCount = articleCount,
                ArticleIds = groupArticles.Select(article => article.Id).ToArray(),
                LatestPublishedAt = latest?.PublishedAt ?? group.LatestPublishedAt,
                RelatedLinks = groupArticles.Take(8).Select(article =>
                {
                    var source = RssSourceCatalog.SourceInfoForUrl(article.FeedUrl);
                    return new RelatedLinkDto
                    {
                        Title = TextCleaner.Clean(article.Title),
                        Source = TextCleaner.Clean(article.Source),
                        Publisher = source.Publisher,
                        FeedUrl = article.FeedUrl,
                        Url = article.Url,
                        ImageUrl = article.ImageUrl,
                        ContentFetchStatus = article.ContentFetchStatus,
                        ContentFetchError = TextCleaner.Clean(article.ContentFetchError)
                    };
                }).ToArray()
            };
        }).OfType<BriefDto>();
    }

    /// <summary>뉴스검색 화면에는 원문 본문 대신 RSS 요약 또는 짧은 로컬 안내 문구만 내려줍니다.</summary>
    private static string BuildSafeSummary(ArticleGroup group, IReadOnlyList<Article> articles, int articleCount, int sourceCount)
    {
        var rssSummary = articles
            .Select(article => TextCleaner.Clean(article.Summary))
            .FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary));

        if (!string.IsNullOrWhiteSpace(rssSummary))
        {
            return rssSummary.Length > 220 ? $"{rssSummary[..220].Trim()}..." : rssSummary;
        }

        var displayArticleCount = articleCount > 0 ? articleCount : group.ArticleCount;
        return $"{displayArticleCount}개 관련 기사가 묶인 이슈입니다. 자세한 내용은 원문 링크에서 확인해 주세요. 확인 출처 {sourceCount}개.";
    }
}
