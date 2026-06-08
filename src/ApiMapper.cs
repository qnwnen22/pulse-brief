using System.Text.RegularExpressions;

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
            var groupArticles = group.ArticleIds.Select(id => byId.GetValueOrDefault(id)).Where(article => article is not null).Cast<Article>().ToArray();
            var latest = groupArticles.FirstOrDefault();
            var minutes = latest is null
                ? 1
                : Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - latest.PublishedAt).TotalMinutes));
            var title = TextCleaner.Clean(group.RepresentativeTitle);
            var previewSource = groupArticles.FirstOrDefault(article => !string.IsNullOrWhiteSpace(article.Content))
                ?? groupArticles.FirstOrDefault(article => !string.IsNullOrWhiteSpace(article.Summary));

            return new BriefDto
            {
                Title = title,
                Category = group.Category,
                Source = TextCleaner.Clean(string.Join(", ", group.Sources.Take(2))),
                Minutes = minutes,
                Impact = group.Score,
                Heat = group.Score >= 80 ? "hot" : "normal",
                Summary = TextCleaner.Clean(group.Summary),
                ContentPreview = BuildPreview(previewSource, title),
                Keywords = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length >= 2).Take(4).ToArray(),
                ArticleCount = group.ArticleCount,
                ArticleIds = group.ArticleIds,
                LatestPublishedAt = group.LatestPublishedAt,
                RelatedLinks = groupArticles.Take(8).Select(article => new RelatedLinkDto
                {
                    Title = TextCleaner.Clean(article.Title),
                    Source = TextCleaner.Clean(article.Source),
                    Url = article.Url,
                    ContentPreview = BuildPreview(article, article.Title, 180),
                    ContentFetchStatus = article.ContentFetchStatus,
                    ContentFetchError = TextCleaner.Clean(article.ContentFetchError)
                }).ToArray()
            };
        });
    }

    /// <summary>기사 본문을 우선 사용하고, 없으면 RSS 요약을 사용해 UI용 짧은 미리보기 문장을 만듭니다.</summary>
    private static string BuildPreview(Article? article, string? title, int maxLength = 320)
    {
        if (article is null) return "";

        var text = NormalizePreviewText(
            TextCleaner.Clean(!string.IsNullOrWhiteSpace(article.Content) ? article.Content : article.Summary),
            TextCleaner.Clean(title));

        if (text.Length <= maxLength) return text;

        var boundary = text.LastIndexOfAny(['.', '!', '?', '다'], Math.Min(text.Length - 1, maxLength));
        if (boundary < maxLength / 2) boundary = maxLength;

        return $"{text[..Math.Min(boundary + 1, text.Length)].Trim()}...";
    }

    /// <summary>기사 본문 앞에 섞인 메뉴, 오디오 플레이어, 광고 표기 등 미리보기 방해 요소를 제거합니다.</summary>
    private static string NormalizePreviewText(string text, string title)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        text = Regex.Replace(text, @"기사를\s*읽어드립니다", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"Your browser does not support the\s*audio element\.?\s*0:00", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (!string.IsNullOrWhiteSpace(title))
        {
            var titleIndex = text.IndexOf(title, StringComparison.OrdinalIgnoreCase);
            if (titleIndex is >= 0 and < 180)
            {
                text = text[(titleIndex + title.Length)..].Trim();
            }
        }

        var adIndex = text.IndexOf("광고", StringComparison.Ordinal);
        if (adIndex is >= 0 and < 280)
        {
            text = text[(adIndex + "광고".Length)..].Trim();
        }

        return Regex.Replace(text, @"^(본문|수정\s*\d{4}[-.]\d{2}[-.]\d{2}\s*\d{1,2}:\d{2})\s*", "", RegexOptions.IgnoreCase).Trim();
    }
}
