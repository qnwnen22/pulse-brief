using System.Net;
using System.Text.RegularExpressions;

namespace PulseBrief;

public static partial class TextCleaner
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var decoded = WebUtility.HtmlDecode(value);
        return HtmlTagRegex().Replace(decoded, " ").ReplaceLineEndings(" ").Trim();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTagRegex();
}
