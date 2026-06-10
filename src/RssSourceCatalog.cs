namespace PulseBrief;

public static class RssSourceCatalog
{
    public static RssSourceInfo SourceInfoForUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new("알 수 없음", null);
        }

        var host = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (host.Contains("yna.co.kr", StringComparison.OrdinalIgnoreCase)) return new("연합뉴스", "https://www.yna.co.kr/rss/index");
        if (host.Contains("yonhapnewstv.co.kr", StringComparison.OrdinalIgnoreCase)) return new("연합뉴스TV", "https://www.yonhapnewstv.co.kr/browse/feed/");
        if (host.Contains("newsis.com", StringComparison.OrdinalIgnoreCase)) return new("뉴시스", "https://www.newsis.com/RSS/");
        if (host.Contains("hankyung.com", StringComparison.OrdinalIgnoreCase)) return new("한국경제", "https://www.hankyung.com/feed");
        if (host.Contains("mk.co.kr", StringComparison.OrdinalIgnoreCase)) return new("매일경제", "https://www.mk.co.kr/rss/");
        if (host.Contains("etnews.com", StringComparison.OrdinalIgnoreCase)) return new("전자신문", "https://rss.etnews.com/");
        if (host.Contains("donga.com", StringComparison.OrdinalIgnoreCase)) return new("동아일보", "https://rss.donga.com/");
        if (host.Contains("hani.co.kr", StringComparison.OrdinalIgnoreCase)) return new("한겨레", "https://www.hani.co.kr/rss/");
        if (host.Contains("khan.co.kr", StringComparison.OrdinalIgnoreCase)) return new("경향신문", "https://www.khan.co.kr/help/help_rss.html");
        if (host.Contains("news.sbs.co.kr", StringComparison.OrdinalIgnoreCase)) return new("SBS 뉴스", "https://news.sbs.co.kr/news/rss.do");
        if (host.Contains("korea.kr", StringComparison.OrdinalIgnoreCase)) return new("정책브리핑", "https://www.korea.kr/etc/rss.do");
        if (host.Contains("imbc.com", StringComparison.OrdinalIgnoreCase)) return new("MBC 뉴스", null);
        if (host.Contains("jtbc.co.kr", StringComparison.OrdinalIgnoreCase)) return new("JTBC 뉴스", "https://news.jtbc.co.kr/rss");
        if (host.Contains("bbc.com", StringComparison.OrdinalIgnoreCase) || host.Contains("bbci.co.uk", StringComparison.OrdinalIgnoreCase)) return new("BBC", "https://www.bbc.com/news/10628494");

        return new(host, null);
    }
}

public sealed record RssSourceInfo(string Publisher, string? GuideUrl);
