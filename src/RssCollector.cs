using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PulseBrief;

/// <summary>RSS/Atom 피드를 내려받아 내부 기사 모델로 변환합니다.</summary>
public sealed class RssCollector(HttpClient httpClient)
{
    /// <summary>여러 RSS 피드 URL을 순회하며 수집 가능한 기사 목록을 반환합니다.</summary>
    public async Task<List<Article>> FetchAsync(IReadOnlyList<string> feedUrls, CancellationToken cancellationToken)
    {
        var articles = new List<Article>();

        foreach (var feedUrl in feedUrls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
                request.Headers.UserAgent.ParseAdd("PulseBrief/0.2 (.NET)");
                using var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                articles.AddRange(Parse(xml, feedUrl));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Console.WriteLine($"[rss] {feedUrl}: {error.Message}");
            }
        }

        return articles;
    }

    /// <summary>RSS 또는 Atom XML 문자열에서 기사 항목을 파싱합니다.</summary>
    private static IEnumerable<Article> Parse(string xml, string feedUrl)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var source = TextCleaner.Clean(document.Root?.Element("channel")?.Element("title")?.Value)
            ?? new Uri(feedUrl).Host;
        if (string.IsNullOrWhiteSpace(source)) source = new Uri(feedUrl).Host;

        var items = document.Descendants("item").ToArray();
        if (items.Length == 0)
        {
            items = document.Root?.Elements().Where(element => element.Name.LocalName == "entry").ToArray() ?? [];
        }

        foreach (var item in items)
        {
            var title = TextCleaner.Clean(ElementValue(item, "title"));
            var url = TextCleaner.Clean(ElementValue(item, "link"));
            if (string.IsNullOrWhiteSpace(url))
            {
                url = item.Elements().FirstOrDefault(element => element.Name.LocalName == "link")?.Attribute("href")?.Value ?? "";
            }

            var summary = TextCleaner.Clean(
                ElementValue(item, "description")
                ?? ElementValue(item, "summary")
                ?? ElementValue(item, "content"));
            var publishedRaw = ElementValue(item, "pubDate") ?? ElementValue(item, "published") ?? ElementValue(item, "updated");
            var publishedAt = DateTimeOffset.TryParse(publishedRaw, out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url)) continue;

            yield return new Article
            {
                Id = IdFor(url, title, publishedAt),
                Title = title,
                Url = url,
                Source = source,
                FeedUrl = feedUrl,
                Summary = summary,
                PublishedAt = publishedAt.ToUniversalTime(),
                FirstSeenAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>XML 항목에서 로컬 이름이 일치하는 첫 번째 자식 요소 값을 찾습니다.</summary>
    private static string? ElementValue(XElement item, string localName)
    {
        return item.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>URL을 우선 사용하고, URL이 없으면 제목과 발행일을 사용해 안정적인 기사 ID를 생성합니다.</summary>
    private static string IdFor(string url, string title, DateTimeOffset publishedAt)
    {
        var source = string.IsNullOrWhiteSpace(url) ? $"{title}:{publishedAt:O}" : url;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
