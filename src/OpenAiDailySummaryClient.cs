using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PulseBrief;

/// <summary>OpenAI Responses API를 호출해 일간/주간 이슈 요약 초안을 AI 요약으로 보강합니다.</summary>
public sealed class OpenAiDailySummaryClient(HttpClient httpClient, IConfiguration configuration)
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>OpenAI API 키가 환경 변수 또는 설정에 존재하는지 여부입니다.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetApiKey());

    /// <summary>로컬 요약 초안을 OpenAI에 전달해 카테고리별 요약과 대표 이슈 요약을 생성합니다.</summary>
    public async Task<DailyIssueSummary?> TryGenerateAsync(DailyIssueSummary draft, string periodLabel = "전날 이슈", CancellationToken cancellationToken = default)
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
                        Summarize the requested Korean news period by category first.
                        Summarize only the facts present in the provided issue data.
                        Do not invent facts, numbers, causes, quotes, or outcomes.
                        When article evidence is provided, use it as the primary source of facts.
                        Prefer issues supported by repeated keywords and multiple independent sources.
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
                    content = BuildPrompt(draft, periodLabel)
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
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Console.WriteLine($"[openai] daily summary failed: {error.Message}");
            return null;
        }
    }

    /// <summary>환경 변수와 설정 파일에서 OpenAI API 키를 조회합니다.</summary>
    private string? GetApiKey()
    {
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? configuration["OpenAI:ApiKey"];
    }

    /// <summary>요약 생성에 사용할 OpenAI 모델명을 설정 우선순위에 따라 결정합니다.</summary>
    private string GetModel()
    {
        return Environment.GetEnvironmentVariable("OPENAI_DAILY_SUMMARY_MODEL")
            ?? configuration["OpenAI:DailySummaryModel"]
            ?? Environment.GetEnvironmentVariable("OPENAI_SUMMARY_MODEL")
            ?? "gpt-5.4-nano";
    }

    /// <summary>요약 초안의 카테고리 분포와 주요 이슈를 OpenAI 입력 프롬프트로 직렬화합니다.</summary>
    private static string BuildPrompt(DailyIssueSummary draft, string periodLabel)
    {
        var topIssues = draft.TopIssues
            .Take(12)
            .Select((issue, index) =>
            {
                var sources = issue.Sources.Length == 0 ? "출처 없음" : string.Join(", ", issue.Sources.Take(4));
                var keywords = issue.Keywords.Length == 0 ? "키워드 없음" : string.Join(", ", issue.Keywords.Take(8));
                var evidence = issue.EvidenceArticles.Length == 0
                    ? "근거 기사 없음"
                    : string.Join("\n", issue.EvidenceArticles.Take(2).Select((article, evidenceIndex) =>
                        $"""
                        - 근거 {evidenceIndex + 1}: {article.Source}
                          제목: {article.Title}
                          RSS 요약: {Compact(article.Summary, 350)}
                          본문 일부: {Compact(article.ContentExcerpt, 900)}
                        """));
                return $"""
                    {index + 1}. [{issue.Category}] {issue.Title}
                    기사수: {issue.ArticleCount}, 점수: {issue.Score}, 출처: {sources}
                    주요 키워드: {keywords}
                    기존 요약: {issue.Summary}
                    근거 기사:
                    {evidence}
                    """;
            });
        var categories = draft.Categories
            .Take(8)
            .Select(category => $"- {category.Category}: 이슈 {category.IssueCount}건, 기사 {category.ArticleCount}건");

        return $"""
            요약 대상: {periodLabel}
            기간 키: {draft.Date}
            전체 이슈 수: {draft.IssueCount}
            전체 기사 수: {draft.ArticleCount}
            전체 출처 수: {draft.SourceCount}

            카테고리 분포:
            {string.Join("\n", categories)}

            주요 이슈:
            {string.Join("\n\n", topIssues)}
            """;
    }

    private static string Compact(string? value, int maxLength)
    {
        var cleaned = TextCleaner.Clean(value ?? "");
        if (cleaned.Length <= maxLength) return cleaned;
        return $"{cleaned[..maxLength].TrimEnd()}...";
    }

    /// <summary>Responses API 응답 JSON에서 모델이 생성한 텍스트 출력을 추출합니다.</summary>
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

    /// <summary>OpenAI가 반환한 JSON 문자열을 기존 요약 초안에 병합합니다.</summary>
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

    /// <summary>AI가 생성한 카테고리별 요약을 기존 카테고리 요약 항목에 반영합니다.</summary>
    private static void ApplyCategorySummaries(DailyIssueSummary draft, JsonArray? categories)
    {
        if (categories is null) return;
        var byCategory = draft.Categories
            .Where(item => !string.IsNullOrWhiteSpace(item.Category))
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var item in categories)
        {
            var category = item?["category"]?.GetValue<string>();
            var summary = item?["summary"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(summary)) continue;
            if (byCategory.TryGetValue(category, out var existing)) existing.Summary = summary.Trim();
        }
    }

    /// <summary>AI가 생성한 대표 이슈별 요약을 기존 대표 이슈 항목에 반영합니다.</summary>
    private static void ApplyIssueSummaries(DailyIssueSummary draft, JsonArray? issues)
    {
        if (issues is null) return;
        var byTitle = draft.TopIssues
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var item in issues)
        {
            var title = item?["title"]?.GetValue<string>();
            var summary = item?["summary"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(summary)) continue;
            if (byTitle.TryGetValue(title, out var existing)) existing.Summary = summary.Trim();
        }
    }

    /// <summary>모델 출력에 설명 문구가 섞였을 때 첫 JSON 객체 부분만 잘라냅니다.</summary>
    private static string ExtractJsonObject(string value)
    {
        var trimmed = value.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) return trimmed;
        return trimmed[start..(end + 1)];
    }
}
