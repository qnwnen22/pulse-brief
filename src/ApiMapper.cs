namespace PulseBrief;

public static class ApiMapper
{
    public static IEnumerable<BriefDto> ToBriefs(IEnumerable<ArticleGroup> groups, IEnumerable<Article> articles)
    {
        var byId = articles.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);

        return groups.Select(group =>
        {
            var groupArticles = group.ArticleIds.Select(id => byId.GetValueOrDefault(id)).Where(article => article is not null).Cast<Article>().ToArray();
            var latest = groupArticles.FirstOrDefault();
            var minutes = latest is null
                ? 1
                : Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - latest.PublishedAt).TotalMinutes));
            var title = TextCleaner.Clean(group.RepresentativeTitle);

            return new BriefDto
            {
                Title = title,
                Category = group.Category,
                Source = TextCleaner.Clean(string.Join(", ", group.Sources.Take(2))),
                Minutes = minutes,
                Impact = group.Score,
                Heat = group.Score >= 80 ? "hot" : "normal",
                Summary = TextCleaner.Clean(group.Summary),
                Keywords = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length >= 2).Take(4).ToArray(),
                ArticleCount = group.ArticleCount,
                ArticleIds = group.ArticleIds,
                LatestPublishedAt = group.LatestPublishedAt,
                RelatedLinks = groupArticles.Take(8).Select(article => new RelatedLinkDto
                {
                    Title = TextCleaner.Clean(article.Title),
                    Source = TextCleaner.Clean(article.Source),
                    Url = article.Url
                }).ToArray()
            };
        });
    }
}
