namespace PulseBrief;

/// <summary>애플리케이션이 사용하는 설정 파일 경로를 계산합니다.</summary>
public sealed class AppPaths(IWebHostEnvironment environment)
{
    private readonly string _root = environment.ContentRootPath;

    /// <summary>RSS 피드 목록 설정 파일 경로입니다.</summary>
    public string FeedsPath => Path.Combine(_root, "config", "rss-feeds.txt");

    /// <summary>RSS 피드 설정 파일에서 주석과 빈 줄을 제외한 피드 URL 목록을 읽습니다.</summary>
    public async Task<IReadOnlyList<string>> ReadFeedUrlsAsync()
    {
        if (!File.Exists(FeedsPath)) return [];

        var lines = await File.ReadAllLinesAsync(FeedsPath);
        return lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
