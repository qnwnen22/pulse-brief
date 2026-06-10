using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PulseBrief;

/// <summary>관리자 토큰 검증, 관리자 세션 쿠키 발급, CSRF 토큰 검증을 담당합니다.</summary>
public sealed class AdminAuthService(IConfiguration configuration)
{
    /// <summary>관리자 세션을 저장하는 HTTP 쿠키 이름입니다.</summary>
    public const string SessionCookieName = "pb_admin_session";

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    /// <summary>요청에 관리자 토큰 헤더 또는 유효한 관리자 세션 쿠키가 있는지 확인합니다.</summary>
    public bool IsAuthenticated(HttpContext context)
    {
        return HasValidHeaderToken(context) || TryReadSession(context, out _) || IsLoopbackAdminAllowed(context);
    }

    /// <summary>관리자 토큰을 검증한 뒤 브라우저용 관리자 세션 쿠키와 CSRF 토큰을 발급합니다.</summary>
    public AdminLoginResult? SignIn(HttpContext context, string? token)
    {
        if (!ValidateConfiguredToken(token)) return null;

        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);
        var cookieValue = CreateSessionCookieValue(sessionId, expiresAt);

        context.Response.Cookies.Append(SessionCookieName, cookieValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = IsHttpsRequest(context),
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expiresAt
        });

        return new AdminLoginResult(true, expiresAt, CreateCsrfToken(sessionId, expiresAt));
    }

    /// <summary>관리자 세션 쿠키를 제거합니다.</summary>
    public void SignOut(HttpContext context)
    {
        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            Secure = IsHttpsRequest(context),
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    /// <summary>현재 관리자 세션에서 API POST/수정 요청에 사용할 CSRF 토큰을 생성합니다.</summary>
    public string? CreateCsrfToken(HttpContext context)
    {
        return TryReadSession(context, out var session)
            ? CreateCsrfToken(session.SessionId, session.ExpiresAt)
            : null;
    }

    /// <summary>관리자 POST/PUT/PATCH/DELETE 요청에 필요한 CSRF 조건을 만족하는지 확인합니다.</summary>
    public bool HasValidCsrf(HttpContext context)
    {
        if (HasValidHeaderToken(context)) return true;
        if (IsLoopbackAdminAllowed(context)) return true;
        if (!TryReadSession(context, out var session)) return false;

        var requestToken = context.Request.Headers["X-CSRF-Token"].FirstOrDefault();
        var expectedToken = CreateCsrfToken(session.SessionId, session.ExpiresAt);
        return TokenEquals(requestToken, expectedToken);
    }

    /// <summary>관리자 API에 대한 인증 실패 응답을 생성합니다.</summary>
    public static IResult AdminRequired()
    {
        return Results.Json(new { error = "admin_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>관리자 API에 대한 CSRF 검증 실패 응답을 생성합니다.</summary>
    public static IResult CsrfRequired()
    {
        return Results.Json(new { error = "csrf_required" }, statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>요청 본문 또는 헤더로 받은 토큰이 설정된 관리자 토큰과 같은지 확인합니다.</summary>
    public bool ValidateConfiguredToken(string? requestToken)
    {
        var configuredToken = GetAdminToken();
        return !string.IsNullOrWhiteSpace(configuredToken) && TokenEquals(requestToken, configuredToken);
    }

    private bool HasValidHeaderToken(HttpContext context)
    {
        var requestToken = context.Request.Headers["X-Admin-Token"].FirstOrDefault();
        return ValidateConfiguredToken(requestToken);
    }

    private string CreateSessionCookieValue(string sessionId, DateTimeOffset expiresAt)
    {
        var payload = $"{sessionId}.{expiresAt.ToUnixTimeSeconds()}";
        return $"{payload}.{Sign(payload)}";
    }

    private bool TryReadSession(HttpContext context, out AdminSession session)
    {
        session = default;
        if (!context.Request.Cookies.TryGetValue(SessionCookieName, out var cookieValue)) return false;

        var parts = cookieValue.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return false;
        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresUnix)) return false;

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);
        if (expiresAt <= DateTimeOffset.UtcNow) return false;

        var payload = $"{parts[0]}.{parts[1]}";
        var expectedSignature = Sign(payload);
        if (!TokenEquals(parts[2], expectedSignature)) return false;

        session = new AdminSession(parts[0], expiresAt);
        return true;
    }

    private string CreateCsrfToken(string sessionId, DateTimeOffset expiresAt)
    {
        return Sign($"csrf.{sessionId}.{expiresAt.ToUnixTimeSeconds()}");
    }

    private string Sign(string payload)
    {
        var token = GetAdminToken();
        if (string.IsNullOrWhiteSpace(token)) return "";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private string? GetAdminToken()
    {
        return Environment.GetEnvironmentVariable("PULSEBRIEF_ADMIN_TOKEN")
            ?? Environment.GetEnvironmentVariable("ADMIN_API_TOKEN")
            ?? configuration["Security:AdminToken"];
    }

    private static bool TokenEquals(string? requestToken, string configuredToken)
    {
        if (string.IsNullOrWhiteSpace(requestToken)) return false;

        var requestBytes = Encoding.UTF8.GetBytes(requestToken);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredToken);
        return requestBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(requestBytes, configuredBytes);
    }

    private static bool IsHttpsRequest(HttpContext context)
    {
        if (context.Request.IsHttps) return true;
        var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        return string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLoopbackAdminAllowed(HttpContext context)
    {
        return configuration.GetValue("Security:AllowLoopbackAdmin", false)
            && context.Connection.RemoteIpAddress is { } remoteIpAddress
            && IPAddress.IsLoopback(remoteIpAddress);
    }
}

/// <summary>관리자 로그인 성공 후 클라이언트에 전달할 세션 정보입니다.</summary>
public sealed record AdminLoginResult(bool Authenticated, DateTimeOffset ExpiresAt, string CsrfToken);

/// <summary>검증된 관리자 세션 쿠키에서 복원한 내부 세션 값입니다.</summary>
public readonly record struct AdminSession(string SessionId, DateTimeOffset ExpiresAt);
