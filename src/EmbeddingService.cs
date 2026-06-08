using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PulseBrief;

/// <summary>기사 제목, RSS 요약, 추출 본문을 기반으로 로컬 임베딩 벡터를 생성합니다.</summary>
public sealed partial class EmbeddingService
{
    private const int VectorSize = 128;

    /// <summary>임베딩이 없거나 비어 있는 기사에 대해 로컬 해시 기반 임베딩을 채웁니다.</summary>
    public Task EnsureEmbeddingsAsync(IEnumerable<Article> articles)
    {
        foreach (var article in articles.Where(article => article.Embedding is null || article.Embedding.Length == 0))
        {
            article.Embedding = LocalEmbedding($"{article.Title}\n{article.Summary}\n{article.Content}");
        }

        return Task.CompletedTask;
    }

    /// <summary>토큰 해시를 고정 길이 벡터에 투영해 간단한 코사인 유사도용 임베딩을 생성합니다.</summary>
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
