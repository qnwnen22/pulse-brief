using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PulseBrief;

public sealed class OpenAiDailySummaryClient(HttpClient httpClient, IConfiguration configuration)
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());

    public async Task<DailyIssueSummary?> TryGenerateAsync(DailyIssueSummary draft, CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey) || draft.IssueCount == 0) return null;

        var model = GetModel();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model,
            max_output_tokens = 1200,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = """
                        You are a Korean news briefing editor.
                        Summarize yesterday's issues by category first.
                        Summarize only the facts present in the provided issue data.
                        Do not invent facts, numbers, causes, quotes, or outcomes.
                        The categories array is the most important output. Write one useful sentence for each major category.
                        Return only valid JSON with this shape:
                        {
                          "headline": "short Korean headline",
                          "summary": "3 concise Korean sentences",
                          "categories": [{"category":"name","summary":"one Korean sentence"}],
                          "topIssues": [{"title":"title","summary":"one Korean sentence"}]
                        }
                        """
                },
                new
                {
                    role = "user",
                    content = BuildPrompt(draft)
                }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[openai] daily summary failed: {(int)response.StatusCode} {error}");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var outputText = ExtractOutputText(body);
            if (string.IsNullOrWhiteSpace(outputText)) return null;

            return ApplyAiResult(draft, outputText, model);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"[openai] daily summary failed: {error.Message}");
            return null;
        }
    }

    private string? GetApiKey()
    {
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? configuration["OpenAI:ApiKey"];
    }

    private string GetModel()
    {
        return Environment.GetEnvironmentVariable("OPENAI_DAILY_SUMMARY_MODEL")
            ?? configuration["OpenAI:DailySummaryModel"]
            ?? Environment.GetEnvironmentVariable("OPENAI_SUMMARY_MODEL")
            ?? "gpt-5.4-nano";
    }

    private static string BuildPrompt(DailyIssueSummary draft)
    {
        var topIssues = draft.TopIssues
            .Take(12)
            .Select((issue, index) =>
            {
                var sources = issue.Sources.Length == 0 ? "출처 없음" : string.Join(", ", issue.Sources.Take(4));
                return $"{index + 1}. [{issue.Category}] {issue.Title}\n기사수: {issue.ArticleCount}, 점수: {issue.Score}, 출처: {sources}\n요약: {issue.Summary}";
            });
        var categories = draft.Categories
            .Take(8)
            .Select(category => $"- {category.Category}: 이슈 {category.IssueCount}건, 기사 {category.ArticleCount}건");

        return $"""
            날짜: {draft.Date}
            전체 이슈 수: {draft.IssueCount}
            전체 기사 수: {draft.ArticleCount}
            전체 출처 수: {draft.SourceCount}

            카테고리 분포:
            {string.Join("\n", categories)}

            주요 이슈:
            {string.Join("\n\n", topIssues)}
            """;
    }

    private static string ExtractOutputText(string body)
    {
        var root = JsonNode.Parse(body);
        var direct = root?["output_text"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(direct)) return direct;

        var builder = new StringBuilder();
        foreach (var item in root?["output"]?.AsArray() ?? [])
        {
            foreach (var content in item?["content"]?.AsArray() ?? [])
            {
                var text = content?["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text)) builder.AppendLine(text);
            }
        }

        return builder.ToString().Trim();
    }

    private DailyIssueSummary? ApplyAiResult(DailyIssueSummary draft, string outputText, string model)
    {
        var json = ExtractJsonObject(outputText);
        var root = JsonNode.Parse(json);
        if (root is null) return null;

        draft.Provider = "openai";
        draft.Model = model;
        draft.GeneratedAt = DateTimeOffset.UtcNow;

        var headline = root["headline"]?.GetValue<string>();
        var summary = root["summary"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(headline)) draft.Headline = headline.Trim();
        if (!string.IsNullOrWhiteSpace(summary)) draft.Summary = summary.Trim();

        ApplyCategorySummaries(draft, root["categories"]?.AsArray());
        ApplyIssueSummaries(draft, root["topIssues"]?.AsArray());

        return draft;
    }

    private static void ApplyCategorySummaries(DailyIssueSummary draft, JsonArray? categories)
    {
        if (categories is null) return;
        var byCategory = draft.Categories.ToDictionary(item => item.Category, StringComparer.OrdinalIgnoreCase);
        foreach (var item in categories)
        {
            var category = item?["category"]?.GetValue<string>();
            var summary = item?["summary"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(summary)) continue;
            if (byCategory.TryGetValue(category, out var existing)) existing.Summary = summary.Trim();
        }
    }

    private static void ApplyIssueSummaries(DailyIssueSummary draft, JsonArray? issues)
    {
        if (issues is null) return;
        var byTitle = draft.TopIssues.ToDictionary(item => item.Title, StringComparer.OrdinalIgnoreCase);
        foreach (var item in issues)
        {
            var title = item?["title"]?.GetValue<string>();
            var summary = item?["summary"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(summary)) continue;
            if (byTitle.TryGetValue(title, out var existing)) existing.Summary = summary.Trim();
        }
    }

    private static string ExtractJsonObject(string value)
    {
        var trimmed = value.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) return trimmed;
        return trimmed[start..(end + 1)];
    }
}
