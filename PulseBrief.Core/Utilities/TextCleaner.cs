using System.Net;
using System.Text;
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
        var plainText = HtmlTagRegex().Replace(decoded, " ").ReplaceLineEndings(" ").Trim();
        return EnsureValidUnicode(plainText);
    }

    /// <summary>잘못된 서로게이트를 치환하고 UTF-16 문자 쌍을 자르지 않도록 최대 길이를 제한합니다.</summary>
    public static string Truncate(string? value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);

        var normalized = EnsureValidUnicode(value ?? "");
        if (normalized.Length <= maxLength) return normalized;
        if (maxLength == 0) return "";

        var length = maxLength;
        if (char.IsHighSurrogate(normalized[length - 1]) &&
            length < normalized.Length &&
            char.IsLowSurrogate(normalized[length]))
        {
            length--;
        }

        return normalized[..length];
    }

    private static string EnsureValidUnicode(string value)
    {
        StringBuilder? result = null;

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (!char.IsSurrogate(current))
            {
                result?.Append(current);
                continue;
            }

            if (char.IsHighSurrogate(current) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                if (result is not null)
                {
                    result.Append(current);
                    result.Append(value[index + 1]);
                }

                index++;
                continue;
            }

            result ??= new StringBuilder(value.Length).Append(value, 0, index);
            result.Append('\uFFFD');
        }

        return result?.ToString() ?? value;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTagRegex();
}
