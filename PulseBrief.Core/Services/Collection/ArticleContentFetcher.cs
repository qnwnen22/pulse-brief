using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace PulseBrief;

/// <summary>기사 원문 URL에 접근해 HTML 본문을 추출하고 수집 성공/실패 상태를 기사 모델에 기록합니다.</summary>
public sealed partial class ArticleContentFetcher(HttpClient httpClient, IConfiguration configuration)
{
    private readonly int _maxArticlesPerRun = Math.Max(1, configuration.GetValue("ArticleContent:MaxArticlesPerRun", 50));
    private readonly int _maxConcurrency = Math.Max(1, configuration.GetValue("ArticleContent:MaxConcurrency", 4));
    private readonly int _minimumContentLength = Math.Max(80, configuration.GetValue("ArticleContent:MinimumContentLength", 220));

    /// <summary>아직 본문 수집을 시도하지 않은 기사 중 최신 기사 일부를 선택해 본문을 보강합니다.</summary>
    public Task EnrichMissingContentAsync(IEnumerable<Article> articles, CancellationToken cancellationToken = default)
    {
        return EnrichMissingContentAsync(articles, _maxArticlesPerRun, cancellationToken);
    }

    /// <summary>아직 본문 수집을 시도하지 않은 기사 중 지정한 개수만큼 선택해 본문을 보강합니다.</summary>
    public async Task EnrichMissingContentAsync(IEnumerable<Article> articles, int limit, CancellationToken cancellationToken = default)
    {
        var targets = articles
            .Where(article => string.IsNullOrWhiteSpace(article.ContentFetchStatus))
            .OrderByDescending(article => article.PublishedAt)
            .Take(Math.Max(1, limit))
            .ToArray();

        await Parallel.ForEachAsync(targets, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _maxConcurrency
        }, async (article, token) => await FetchContentAsync(article, token));
    }

    /// <summary>대표 이미지가 비어 있는 기사 중 지정한 개수만큼 선택해 원문 페이지의 og:image를 보강합니다.</summary>
    public async Task EnrichMissingImagesAsync(IEnumerable<Article> articles, int limit, CancellationToken cancellationToken = default)
    {
        var targets = articles
            .Where(article => string.IsNullOrWhiteSpace(article.ImageUrl))
            .OrderByDescending(article => article.PublishedAt)
            .Take(Math.Max(1, limit))
            .ToArray();

        await Parallel.ForEachAsync(targets, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _maxConcurrency
        }, async (article, token) => await FetchImageAsync(article, token));
    }

    /// <summary>단일 기사 URL에서 본문을 가져오고 성공 또는 실패 상태를 Article에 반영합니다.</summary>
    private async Task FetchContentAsync(Article article, CancellationToken cancellationToken)
    {
        article.ContentFetchedAt = DateTimeOffset.UtcNow;

        if (!Uri.TryCreate(article.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MarkFailed(article, "기사 링크 형식이 올바르지 않아 본문을 가져오지 못했습니다.");
            return;
        }

        var targetError = await ValidateFetchTargetAsync(uri, cancellationToken);
        if (!string.IsNullOrWhiteSpace(targetError))
        {
            MarkFailed(article, targetError);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("PulseBrief/0.3 (+local news summarizer)");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            request.Headers.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9,en;q=0.6");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                MarkFailed(article, $"기사 페이지가 HTTP {(int)response.StatusCode} 응답을 반환해 본문을 가져오지 못했습니다.");
                return;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!string.IsNullOrWhiteSpace(mediaType) && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                MarkFailed(article, "기사 링크가 HTML 문서가 아니라 본문을 추출하지 못했습니다.");
                return;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            article.ImageUrl = string.IsNullOrWhiteSpace(article.ImageUrl)
                ? ExtractImageUrl(html, uri)
                : article.ImageUrl;
            var content = ExtractArticleText(html);
            if (content.Length < _minimumContentLength)
            {
                MarkFailed(article, "기사 본문 영역을 충분히 찾지 못했습니다. RSS 요약으로 대체합니다.");
                return;
            }

            article.Content = content;
            article.ContentFetchStatus = "success";
            article.ContentFetchError = "";
            article.Embedding = null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            MarkFailed(article, $"기사 페이지에 접근하는 중 문제가 발생했습니다: {error.Message}");
        }
    }

    /// <summary>단일 기사 URL의 HTML 메타 태그에서 대표 이미지만 추출해 Article에 반영합니다.</summary>
    private async Task FetchImageAsync(Article article, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(article.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        if (await ValidateFetchTargetAsync(uri, cancellationToken) is not null) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("PulseBrief/0.3 (+local news summarizer)");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            request.Headers.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9,en;q=0.6");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return;

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!string.IsNullOrWhiteSpace(mediaType) && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)) return;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            article.ImageUrl = ExtractImageUrl(html, uri);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Console.WriteLine($"[image] {article.Url}: {error.Message}");
        }
    }

    /// <summary>다운로드한 HTML에서 기사 본문으로 보이는 가장 긴 후보 텍스트를 추출합니다.</summary>
    private static string ExtractArticleText(string html)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);

        foreach (var node in document.QuerySelectorAll("script, style, noscript, iframe, nav, aside, footer, header, form, button"))
        {
            node.Remove();
        }

        var candidates = new List<string>();
        var selectors = new[]
        {
            "article",
            "main article",
            "[itemprop='articleBody']",
            ".article-body",
            ".articleBody",
            ".news_view",
            ".news-view",
            ".view_cont",
            ".view-content",
            ".article_view",
            ".article-content",
            ".article_txt",
            ".article-text",
            ".news_text",
            ".news-content",
            "#articleBody",
            "#article_body",
            "#news_body_area",
            "#dic_area",
            "#articeBody",
            "#article-view-content-div"
        };

        foreach (var selector in selectors)
        {
            candidates.AddRange(document.QuerySelectorAll(selector).Select(TextFrom));
        }

        candidates.AddRange(document.QuerySelectorAll("p")
            .Select(TextFrom)
            .Where(text => text.Length >= 40));

        var best = candidates
            .Select(NormalizeText)
            .Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(text => text.Length)
            .FirstOrDefault() ?? "";

        return best.Length > 12000 ? best[..12000] : best;
    }

    /// <summary>기사 HTML에서 og:image, twitter:image, 주요 image_src 값을 대표 이미지 URL로 추출합니다.</summary>
    private static string ExtractImageUrl(string html, Uri articleUri)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var selectors = new[]
        {
            "meta[property='og:image']",
            "meta[property='og:image:url']",
            "meta[name='twitter:image']",
            "meta[name='twitter:image:src']",
            "link[rel='image_src']"
        };

        foreach (var selector in selectors)
        {
            var element = document.QuerySelector(selector);
            var value = element?.GetAttribute("content") ?? element?.GetAttribute("href");
            var normalized = NormalizeImageUrl(value, articleUri);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        }

        var articleImage = document.QuerySelector("article img, main img, .article img, .news_view img")?.GetAttribute("src");
        return NormalizeImageUrl(articleImage, articleUri);
    }

    /// <summary>상대 이미지 주소를 기사 URL 기준의 절대 URL로 변환하고 http/https만 허용합니다.</summary>
    private static string NormalizeImageUrl(string? imageUrl, Uri articleUri)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return "";
        if (!Uri.TryCreate(articleUri, imageUrl.Trim(), out var uri)) return "";
        if (IsClearlyUnsafeUri(uri)) return "";

        return uri.Scheme is "http" or "https" ? uri.ToString() : "";
    }

    /// <summary>서버가 외부 URL에 접근하기 전에 내부망, localhost, 비공개 IP 대역을 가리키는지 확인합니다.</summary>
    private static async Task<string?> ValidateFetchTargetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return "기사 링크가 HTTP/HTTPS 주소가 아니라 본문을 가져오지 못했습니다.";
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo) || IsBlockedHostName(uri.Host))
        {
            return "기사 링크가 안전하지 않은 주소를 가리켜 본문을 가져오지 못했습니다.";
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return $"기사 도메인을 확인하지 못해 본문을 가져오지 못했습니다: {error.Message}";
        }

        if (addresses.Length == 0 || addresses.Any(IsNonPublicAddress))
        {
            return "기사 링크가 내부 네트워크 주소를 가리켜 본문을 가져오지 못했습니다.";
        }

        return null;
    }

    /// <summary>이미지 URL처럼 비동기 DNS 검사가 어려운 값에서 명백히 위험한 호스트를 빠르게 차단합니다.</summary>
    private static bool IsClearlyUnsafeUri(Uri uri)
    {
        return uri.Scheme is not ("http" or "https")
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || IsBlockedHostName(uri.Host);
    }

    /// <summary>localhost, .local, IP 리터럴 기반의 비공개 주소를 차단합니다.</summary>
    private static bool IsBlockedHostName(string host)
    {
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized is "localhost" or "localhost.localdomain") return true;
        if (normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

        return IPAddress.TryParse(normalized, out var address) && IsNonPublicAddress(address);
    }

    /// <summary>루프백, 사설망, 링크로컬, 멀티캐스트 등 외부 기사 수집 대상이 되면 안 되는 주소인지 확인합니다.</summary>
    private static bool IsNonPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] >= 224
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
                || bytes[0] == 198 && bytes[1] is 18 or 19;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || address.Equals(IPAddress.IPv6Loopback)
                || bytes[0] is 0xfc or 0xfd;
        }

        return true;
    }

    /// <summary>AngleSharp 요소의 텍스트 콘텐츠를 안전하게 반환합니다.</summary>
    private static string TextFrom(IElement element)
    {
        return element.TextContent ?? "";
    }

    /// <summary>본문 후보 텍스트의 HTML 흔적과 중복 공백을 정리합니다.</summary>
    private static string NormalizeText(string value)
    {
        var cleaned = TextCleaner.Clean(value);
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();
        return cleaned;
    }

    /// <summary>본문 수집 실패 상태와 사용자에게 보여줄 오류 메시지를 기사에 기록합니다.</summary>
    private static void MarkFailed(Article article, string message)
    {
        article.Content = "";
        article.ContentFetchStatus = "failed";
        article.ContentFetchError = message;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
