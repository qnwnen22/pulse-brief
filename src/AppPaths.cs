namespace PulseBrief;

/// <summary>애플리케이션이 사용하는 데이터, 설정, 마이그레이션 파일 경로를 계산합니다.</summary>
public sealed class AppPaths(IWebHostEnvironment environment)
{
    private readonly string _root = environment.ContentRootPath;

    /// <summary>SQLite DB와 과거 JSON 데이터 파일을 저장하는 data 디렉터리 경로입니다.</summary>
    public string DataDirectory => Path.Combine(_root, "data");

    /// <summary>SQLite 저장소를 사용할 때의 데이터베이스 파일 경로입니다.</summary>
    public string DatabasePath => Path.Combine(DataDirectory, "pulsebrief.db");

    /// <summary>초기 SQLite 마이그레이션용 과거 기사 JSON 파일 경로입니다.</summary>
    public string ArticlesPath => Path.Combine(DataDirectory, "articles.json");

    /// <summary>초기 SQLite 마이그레이션용 과거 그룹 JSON 파일 경로입니다.</summary>
    public string GroupsPath => Path.Combine(DataDirectory, "groups.json");

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
