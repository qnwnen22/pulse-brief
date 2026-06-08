namespace PulseBrief;

public sealed class AppPaths(IWebHostEnvironment environment)
{
    private readonly string _root = environment.ContentRootPath;

    public string DataDirectory => Path.Combine(_root, "data");
    public string DatabasePath => Path.Combine(DataDirectory, "pulsebrief.db");
    public string ArticlesPath => Path.Combine(DataDirectory, "articles.json");
    public string GroupsPath => Path.Combine(DataDirectory, "groups.json");
    public string FeedsPath => Path.Combine(_root, "config", "rss-feeds.txt");

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
