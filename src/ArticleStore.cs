using System.Text.Json;

namespace PulseBrief;

public sealed class ArticleStore(AppPaths paths)
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<List<Article>> ReadArticlesAsync()
    {
        return await ReadJsonAsync(paths.ArticlesPath, new List<Article>());
    }

    public async Task<List<ArticleGroup>> ReadGroupsAsync()
    {
        return await ReadJsonAsync(paths.GroupsPath, new List<ArticleGroup>());
    }

    public async Task SaveArticlesAsync(IReadOnlyCollection<Article> articles)
    {
        await WriteJsonAsync(paths.ArticlesPath, articles);
    }

    public async Task SaveGroupsAsync(IReadOnlyCollection<ArticleGroup> groups)
    {
        await WriteJsonAsync(paths.GroupsPath, groups);
    }

    public async Task<List<Article>> UpsertArticlesAsync(IReadOnlyCollection<Article> incoming)
    {
        var current = await ReadArticlesAsync();
        var byId = current.ToDictionary(article => article.Id, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        foreach (var article in incoming)
        {
            if (byId.TryGetValue(article.Id, out var previous))
            {
                article.Embedding ??= previous.Embedding;
                article.FirstSeenAt = previous.FirstSeenAt;
            }

            article.UpdatedAt = now;
            byId[article.Id] = article;
        }

        var merged = byId.Values
            .OrderByDescending(article => article.PublishedAt)
            .ThenByDescending(article => article.FirstSeenAt)
            .ToList();

        await SaveArticlesAsync(merged);
        return merged;
    }

    private async Task<T> ReadJsonAsync<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions) ?? fallback;
    }

    private async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, _jsonOptions);
    }
}
