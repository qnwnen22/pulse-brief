using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PulseBrief;

public sealed partial class EmbeddingService
{
    private const int VectorSize = 128;

    public Task EnsureEmbeddingsAsync(IEnumerable<Article> articles)
    {
        foreach (var article in articles.Where(article => article.Embedding is null || article.Embedding.Length == 0))
        {
            article.Embedding = LocalEmbedding($"{article.Title}\n{article.Summary}");
        }

        return Task.CompletedTask;
    }

    private static double[] LocalEmbedding(string text)
    {
        var vector = new double[VectorSize];
        var tokens = TokenRegex().Matches(text.ToLowerInvariant()).Select(match => match.Value);

        foreach (var token in tokens)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            for (var i = 0; i < 4; i++)
            {
                var index = hash[i] % VectorSize;
                vector[index] += hash[i + 4] > 127 ? 1 : -1;
            }
        }

        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude <= 0) return vector;

        for (var i = 0; i < vector.Length; i++) vector[i] /= magnitude;
        return vector;
    }

    [GeneratedRegex("[a-z0-9가-힣]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();
}
