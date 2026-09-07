namespace PulseBrief;

/// <summary>이슈 피드에서 사용하는 중요도, HOT 여부 같은 노출 신호를 계산합니다.</summary>
public static class IssueSignalCalculator
{
    /// <summary>중복 제거된 기사 수와 출처 수를 기반으로 이슈 중요도 점수를 계산합니다.</summary>
    public static int CalculateImpact(int effectiveArticleCount, int effectiveSourceCount)
    {
        if (effectiveArticleCount <= 0) return 0;
        var score = Math.Min(100, 30 + effectiveArticleCount * 4 + effectiveSourceCount * 3);
        var sourceCap = effectiveSourceCount switch
        {
            <= 1 => 55,
            2 => 68,
            3 => 78,
            _ => 100
        };

        return Math.Min(score, sourceCap);
    }

    /// <summary>제목과 출처 특성을 함께 반영해 중요도 점수를 계산합니다.</summary>
    public static int CalculateImpact(int effectiveArticleCount, int effectiveSourceCount, string title, IEnumerable<string> sources)
    {
        var score = CalculateImpact(effectiveArticleCount, effectiveSourceCount);
        return IsLowBriefingValue(title, sources) ? Math.Min(score, 45) : score;
    }

    /// <summary>중요도 점수를 기준으로 이슈 피드의 HOT 표시 여부를 판단합니다.</summary>
    public static string HeatFromImpact(int impact)
    {
        return impact >= 80 ? "hot" : "normal";
    }

    /// <summary>포토/화보처럼 기사 수만으로 시사 흐름의 중요도를 판단하기 어려운 이슈인지 확인합니다.</summary>
    public static bool IsLowBriefingValue(string title, IEnumerable<string>? sources = null)
    {
        var sourceText = sources is null ? "" : string.Join(' ', sources);
        var text = $"{sourceText} {title}".ToLowerInvariant();
        return text.Contains("et포토", StringComparison.OrdinalIgnoreCase)
            || text.Contains("포토", StringComparison.OrdinalIgnoreCase)
            || text.Contains("화보", StringComparison.OrdinalIgnoreCase);
    }
}
