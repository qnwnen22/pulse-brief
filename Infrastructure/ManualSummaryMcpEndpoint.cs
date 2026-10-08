using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace PulseBrief;

/// <summary>Sessionless MCP JSON-RPC transport exposing only the two manual-summary operations.</summary>
public static class ManualSummaryMcpEndpoint
{
    private const string Path = "/mcp";
    private const string ProtocolVersion = "2025-03-26";
    private const int MaxRequestBytes = 3 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app, CloudflareAccessSettings settings)
    {
        // This hostname is an isolated virtual host. Keep the existing site, APIs, and admin
        // routes unreachable there, and keep /mcp unreachable on the ordinary site hostname.
        app.Use(async (context, next) =>
        {
            var isMcpHost = string.Equals(context.Request.Host.Host, settings.Hostname, StringComparison.OrdinalIgnoreCase);
            if (!isMcpHost && string.Equals(context.Request.Path.Value, Path, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            if (isMcpHost)
            {
                if (!string.Equals(context.Request.Path.Value, Path, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                if (!HttpMethods.IsPost(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    context.Response.Headers.Allow = "POST";
                    return;
                }
            }
            await next(context);
        });

        app.MapPost(Path, HandleAsync)
            .RequireHost(settings.Hostname)
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        CloudflareAccessJwtAuthenticator authenticator,
        ManualSummaryMcpService service,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > MaxRequestBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!context.Request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

        // Authenticate before reading/parsing any caller-controlled tool arguments.
        CloudflareAccessIdentity? identity;
        try { identity = await authenticator.AuthenticateAsync(context, cancellationToken); }
        catch (Exception ex) when (ex is HttpRequestException or SecurityTokenException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (identity is null) return Results.Unauthorized();

        JsonDocument request;
        try
        {
            request = await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow }, cancellationToken);
        }
        catch (JsonException)
        {
                return Results.Json(Error(default, -32700, "Parse error"), statusCode: 400);
        }

        using (request)
        {
            var root = request.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jsonrpc", out var version)
                || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0"
                || !root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String)
                return Results.Json(Error(ReadId(root), -32600, "Invalid Request"), statusCode: 400);

            var method = methodElement.GetString()!;
            var hasId = root.TryGetProperty("id", out var requestId);
            var id = hasId ? requestId.Clone() : default;
            if (hasId && id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
                return Results.Json(Error(default, -32600, "Invalid Request"), statusCode: 400);
            if (method == "notifications/initialized" && !hasId)
                return Results.Accepted();
            if (!hasId) return Results.Accepted();

            if (method == "ping") return Results.Json(Success(id, new { }));
            if (method == "initialize")
            {
                var requested = root.TryGetProperty("params", out var initParams)
                    && initParams.ValueKind == JsonValueKind.Object
                    && initParams.TryGetProperty("protocolVersion", out var pv)
                    && pv.ValueKind == JsonValueKind.String
                    ? pv.GetString() : null;
                var acceptedVersion = requested is "2025-03-26" or "2024-11-05" ? requested : ProtocolVersion;
                return Results.Json(Success(id, new
                {
                    protocolVersion = acceptedVersion,
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "pulsebrief-daily-summary", version = "0.1.0" }
                }));
            }
            if (method == "tools/list") return Results.Json(Success(id, ToolList()));
            if (method == "tools/call")
            {
                try
                {
                    var (name, arguments) = ReadToolCall(root);
                    object result = name switch
                    {
                        "pulsebrief_export_daily_articles" => await ExportAsync(service, arguments, cancellationToken),
                        "pulsebrief_publish_daily_summary" => await PublishAsync(service, arguments, cancellationToken),
                        _ => throw new ToolInputException("Unknown tool.")
                    };
                    return Results.Json(Success(id, new
                    {
                        content = new[] { new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) } },
                        isError = false
                    }));
                }
                catch (ToolInputException ex)
                {
                    return Results.Json(Success(id, new
                    {
                        content = new[] { new { type = "text", text = ex.Message } }, isError = true
                    }));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or FormatException)
                {
                    return Results.Json(Success(id, new
                    {
                        content = new[] { new { type = "text", text = "Request rejected by daily-summary validation." } }, isError = true
                    }));
                }
            }
            return Results.Json(Error(id, -32601, "Method not found"));
        }
    }

    private static async Task<ManualSummaryArticleExport> ExportAsync(
        ManualSummaryMcpService service, JsonElement args, CancellationToken cancellationToken)
    {
        RequireObject(args, "date", "maxArticles", "cursor");
        var date = RequiredString(args, "date");
        var max = args.TryGetProperty("maxArticles", out var maxValue)
            ? maxValue.GetInt32() : ManualSummaryMcpService.DefaultArticleLimit;
        if (max is < 1 or > ManualSummaryMcpService.DefaultArticleLimit)
            throw new ToolInputException("maxArticles must be between 1 and 10000.");
        string? cursor = null;
        if (args.TryGetProperty("cursor", out var cursorValue))
        {
            if (cursorValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cursorValue.GetString()))
                throw new ToolInputException("cursor must be a non-empty continuation token.");
            cursor = cursorValue.GetString();
        }
        return await service.ExportDailyArticlesAsync(date, max, cursor, cancellationToken);
    }

    private static async Task<ManualSummaryPublication> PublishAsync(
        ManualSummaryMcpService service, JsonElement args, CancellationToken cancellationToken)
    {
        RequireObject(args, "date", "summary");
        var date = RequiredString(args, "date");
        if (!args.TryGetProperty("summary", out var summaryElement) || summaryElement.ValueKind != JsonValueKind.Object)
            throw new ToolInputException("summary must be a daily summary object.");
        var summary = summaryElement.Deserialize<DailyIssueSummary>(JsonOptions)
            ?? throw new ToolInputException("summary must be a daily summary object.");
        return await service.PublishDailySummaryAsync(date, summary, cancellationToken);
    }

    private static object ToolList() => new
    {
        tools = new object[]
        {
            new
            {
                name = "pulsebrief_export_daily_articles",
                description = "Export a bounded page from the complete, non-excluded articles published on a past Korea date. Pass nextCursor until complete is true; verify page totals, snapshotAt, hashes and unique IDs before summarizing. Reuse the same maxArticles and cursor for every page. Existing summaries return no articles.",
                inputSchema = new { type = "object", properties = new { date = new { type = "string", format = "date" }, maxArticles = new { type = "integer", minimum = 1, maximum = 10000 }, cursor = new { type = "string", maxLength = 8192 } }, required = new[] { "date" }, additionalProperties = false }
            },
            new
            {
                name = "pulsebrief_publish_daily_summary",
                description = "Insert and verify a manual summary for a Korea date without replacing an existing summary.",
                inputSchema = new { type = "object", properties = new { date = new { type = "string", format = "date" }, summary = new { type = "object" } }, required = new[] { "date", "summary" }, additionalProperties = false }
            }
        }
    };

    private static (string Name, JsonElement Arguments) ReadToolCall(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
            || !parameters.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
            throw new ToolInputException("Tool name and object arguments are required.");
        return (name.GetString()!, arguments);
    }

    private static void RequireObject(JsonElement args, params string[] allowedNames)
    {
        if (args.ValueKind != JsonValueKind.Object) throw new ToolInputException("Tool arguments must be an object.");
        var allowed = allowedNames.ToHashSet(StringComparer.Ordinal);
        if (args.EnumerateObject().Any(property => !allowed.Contains(property.Name)))
            throw new ToolInputException("Unknown tool argument.");
    }

    private static string RequiredString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new ToolInputException($"{name} is required.");

    private static JsonElement ReadId(JsonElement root) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("id", out var id) ? id.Clone() : default;

    private static object Success(JsonElement id, object result) => new { jsonrpc = "2.0", id, result };
    private static object Error(JsonElement id, int code, string message) => new { jsonrpc = "2.0", id = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id, error = new { code, message } };
    private sealed class ToolInputException(string message) : Exception(message);
}
