namespace PulseBrief;

/// <summary>동일 출처, 제목, 작성자로 반복 수집된 기사를 화면과 요약 지표에서 하나로 취급하기 위한 유틸리티입니다.</summary>
public static class ArticleDedupe
{
    /// <summary>기사 목록에서 동일 출처, 정규화된 제목, 작성자가 같은 항목을 하나만 남기고 최신 기사 순서로 반환합니다.</summary>
    public static Article[] EffectiveArticles(IEnumerable<Article?> articles)
    {
        return articles
            .Where(article => article is not null)
            .Cast<Article>()
            .GroupBy(DuplicateKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(article => article.PublishedAt)
                .ThenByDescending(article => article.FirstSeenAt)
                .First())
            .OrderByDescending(article => article.PublishedAt)
            .ThenByDescending(article => article.FirstSeenAt)
            .ToArray();
    }

    /// <summary>이슈 그룹의 기사 ID 목록을 실제 기사 문서로 해석한 뒤 중복 제거된 기사 ID만 반환합니다.</summary>
    public static string[] EffectiveArticleIds(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        return EffectiveArticles(group.ArticleIds.Select(id => articleById.TryGetValue(id, out var article) ? article : null))
            .Select(article => article.Id)
            .ToArray();
    }

    /// <summary>이슈 그룹에서 중복 기사를 제외한 유효 기사 수를 계산합니다.</summary>
    public static int EffectiveArticleCount(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        return EffectiveArticleIds(group, articleById).Length;
    }

    /// <summary>이슈 그룹에서 중복 기사를 제외한 뒤 확인되는 고유 출처 수를 계산합니다.</summary>
    public static int EffectiveSourceCount(ArticleGroup group, IReadOnlyDictionary<string, Article> articleById)
    {
        return EffectiveArticles(group.ArticleIds.Select(id => articleById.TryGetValue(id, out var article) ? article : null))
            .Select(article => TextCleaner.Clean(article.Source))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>동일 기사 반복 여부를 판단하기 위한 출처, 정규화 제목, 작성자 기반 키를 만듭니다.</summary>
    public static string DuplicateKey(Article article)
    {
        var author = string.IsNullOrWhiteSpace(article.Author) ? "unknown" : TextCleaner.Clean(article.Author).ToLowerInvariant();
        return $"{TextCleaner.Clean(article.Source).ToLowerInvariant()}|{NormalizeForDuplicate(article.Title)}|{author}";
    }

    /// <summary>중복 판정에서 문장부호와 공백 차이를 줄이기 위해 제목을 소문자와 문자/숫자만 남긴 형태로 정규화합니다.</summary>
    private static string NormalizeForDuplicate(string value)
    {
        var cleaned = TextCleaner.Clean(value).ToLowerInvariant();
        return new string(cleaned.Where(char.IsLetterOrDigit).ToArray());
    }
}
