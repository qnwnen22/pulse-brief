namespace PulseBrief;

/// <summary>애플리케이션이 사용하는 설정 파일 경로를 계산합니다.</summary>
public sealed class AppPaths(IHostEnvironment environment)
{
    private readonly string _root = environment.ContentRootPath;

    /// <summary>RSS 피드 목록 설정 파일 경로입니다.</summary>
    public string FeedsPath => Path.Combine(_root, "config", "rss-feeds.txt");

    /// <summary>RSS 피드 설정 파일에서 주석과 빈 줄을 제외한 피드 URL 목록을 읽습니다.</summary>
    public async Task<IReadOnlyList<string>> ReadFeedUrlsAsync()
    {
        return (await ReadFeedEntriesAsync())
            .Where(feed => feed.IsActive)
            .Select(feed => feed.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>RSS 설정 파일에서 활성/비활성 상태를 포함한 전체 피드 목록을 읽습니다.</summary>
    public async Task<IReadOnlyList<RssFeedEntry>> ReadFeedEntriesAsync()
    {
        if (!File.Exists(FeedsPath)) return [];

        var lines = await File.ReadAllLinesAsync(FeedsPath);
        return lines
            .Select(ParseFeedLine)
            .Where(feed => feed is not null)
            .Cast<RssFeedEntry>()
            .GroupBy(feed => feed.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>관리자 화면에서 편집한 RSS 피드 목록을 설정 파일에 저장합니다.</summary>
    public async Task SaveFeedEntriesAsync(IEnumerable<RssFeedEntry> entries)
    {
        var normalized = entries
            .Select(feed => new RssFeedEntry(NormalizeUrl(feed.Url), feed.IsActive))
            .Where(feed => !string.IsNullOrWhiteSpace(feed.Url))
            .GroupBy(feed => feed.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(feed => feed.IsActive ? feed.Url : $"# disabled {feed.Url}")
            .ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(FeedsPath)!);
        await File.WriteAllLinesAsync(FeedsPath, normalized);
    }

    private static RssFeedEntry? ParseFeedLine(string line)
    {
        var value = line.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var disabledUrl = value.TrimStart('#').Trim();
            if (disabledUrl.StartsWith("disabled", StringComparison.OrdinalIgnoreCase))
            {
                disabledUrl = disabledUrl["disabled".Length..].Trim();
            }

            var normalizedDisabledUrl = NormalizeUrl(disabledUrl);
            return string.IsNullOrWhiteSpace(normalizedDisabledUrl) ? null : new RssFeedEntry(normalizedDisabledUrl, false);
        }

        var normalizedUrl = NormalizeUrl(value);
        return string.IsNullOrWhiteSpace(normalizedUrl) ? null : new RssFeedEntry(normalizedUrl, true);
    }

    private static string NormalizeUrl(string value)
    {
        var url = value.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        return uri.Scheme is "http" or "https" ? uri.ToString() : "";
    }
}

/// <summary>관리자가 관리하는 RSS 피드 URL과 활성 상태입니다.</summary>
public sealed record RssFeedEntry(string Url, bool IsActive);
