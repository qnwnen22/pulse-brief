using System.Net;
using System.Text.RegularExpressions;

namespace PulseBrief;

/// <summary>RSS, HTML, AI 응답에서 나온 텍스트를 화면과 저장소에 적합한 일반 문자열로 정리합니다.</summary>
public static partial class TextCleaner
{
    /// <summary>HTML 엔티티를 디코딩하고 태그와 줄바꿈을 제거한 텍스트를 반환합니다.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var decoded = WebUtility.HtmlDecode(value);
        return HtmlTagRegex().Replace(decoded, " ").ReplaceLineEndings(" ").Trim();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTagRegex();
}
