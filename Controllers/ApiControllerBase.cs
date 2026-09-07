using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected static string AdminIndexPath(IWebHostEnvironment environment)
    {
        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        return Path.Combine(webRoot, "admin", "index.html");
    }

    protected static bool RequireAdmin(HttpContext context, AdminAuthService adminAuth, bool requireCsrf, out IResult denied)
    {
        if (!adminAuth.IsAuthenticated(context))
        {
            denied = AdminAuthService.AdminRequired();
            return false;
        }

        if (requireCsrf && !adminAuth.HasValidCsrf(context))
        {
            denied = AdminAuthService.CsrfRequired();
            return false;
        }

        denied = Results.Empty;
        return true;
    }

    protected static IResult SummaryGenerationDisabled()
    {
        return Results.Json(
            new { error = "summary_generation_disabled", message = "Summary generation is disabled." },
            statusCode: StatusCodes.Status409Conflict);
    }


    protected static bool TryNormalizeUrl(string value, out string url)
    {
        url = "";
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        url = uri.ToString();
        return true;
    }

    protected static bool TryParseDate(string? value, string parameterName, out DateOnly? date, out IResult? error)
    {
        date = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (DateOnly.TryParse(value, out var parsed))
        {
            date = parsed;
            return true;
        }

        error = Results.BadRequest(new { error = $"{parameterName}_must_be_yyyy_mm_dd" });
        return false;
    }


    protected static object CreateLocalError(HttpContext context, Exception error)
    {
        var isLoopback = context.Connection.RemoteIpAddress is { } remoteIpAddress && IPAddress.IsLoopback(remoteIpAddress);
        return isLoopback
            ? new { error = error.GetType().Name, message = error.Message }
            : new { error = "summary_failed" };
    }

}
