using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace PulseBrief;

public sealed class CloudflareAccessJwtAuthenticator(IConfiguration configuration, HttpClient httpClient)
{
    private const string HeaderName = "Cf-Access-Jwt-Assertion";
    private static readonly TimeSpan SigningKeyCacheLifetime = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private IReadOnlyCollection<SecurityKey> _signingKeys = [];
    private DateTimeOffset _signingKeysExpireAt = DateTimeOffset.MinValue;

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue("Mcp:DailySummary:Enabled", false);

    public static CloudflareAccessSettings ReadSettings(IConfiguration configuration)
    {
        var teamDomain = configuration["Mcp:CloudflareAccess:TeamDomain"]?.TrimEnd('/');
        var hostname = configuration["Mcp:CloudflareAccess:Hostname"]?.Trim().TrimEnd('.');
        var audience = configuration["Mcp:CloudflareAccess:Audience"]?.Trim();
        var allowedEmails = configuration.GetSection("Mcp:CloudflareAccess:AllowedEmails").Get<string[]>() ?? [];
        if (!Uri.TryCreate(teamDomain, UriKind.Absolute, out var teamUri)
            || teamUri.Scheme != Uri.UriSchemeHttps
            || !teamUri.Host.EndsWith(".cloudflareaccess.com", StringComparison.OrdinalIgnoreCase)
            || teamUri.AbsolutePath != "/" || !string.IsNullOrEmpty(teamUri.Query) || !string.IsNullOrEmpty(teamUri.Fragment))
            throw new InvalidOperationException("MCP is enabled but the Cloudflare Access team domain is invalid.");
        if (string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("MCP is enabled but the Cloudflare Access application audience is missing.");
        if (string.IsNullOrWhiteSpace(hostname) || !Uri.CheckHostName(hostname).Equals(UriHostNameType.Dns)
            || hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MCP is enabled but its dedicated hostname is invalid.");
        var emails = allowedEmails.Select(email => email.Trim()).Where(email => email.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (emails.Length == 0 || emails.Any(email => email.Contains('@') is false || email.Any(char.IsWhiteSpace)))
            throw new InvalidOperationException("MCP is enabled but no valid allowed identity is configured.");
        return new CloudflareAccessSettings(teamUri.GetLeftPart(UriPartial.Authority), hostname, audience, emails);
    }

    public async Task<CloudflareAccessIdentity?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var settings = ReadSettings(configuration);
        var token = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)) return null;

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var keys = await GetSigningKeysAsync(settings.TeamDomain, forceRefresh: false, cancellationToken);
        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(token, ValidationParameters(settings, keys), out _);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            keys = await GetSigningKeysAsync(settings.TeamDomain, forceRefresh: true, cancellationToken);
            try { principal = handler.ValidateToken(token, ValidationParameters(settings, keys), out _); }
            catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return null; }
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return null; }

        var email = principal.FindFirst("email")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email)
            || !settings.AllowedEmails.Contains(email, StringComparer.OrdinalIgnoreCase)) return null;
        return new CloudflareAccessIdentity(email);
    }

    private TokenValidationParameters ValidationParameters(CloudflareAccessSettings settings, IEnumerable<SecurityKey> keys) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKeys = keys,
        ValidateIssuer = true,
        ValidIssuer = settings.TeamDomain,
        ValidateAudience = true,
        ValidAudience = settings.Audience,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(60),
        ValidAlgorithms = ["RS256"],
        NameClaimType = "email"
    };

    private async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        string teamDomain, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _signingKeys.Count > 0 && DateTimeOffset.UtcNow < _signingKeysExpireAt) return _signingKeys;
        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _signingKeys.Count > 0 && DateTimeOffset.UtcNow < _signingKeysExpireAt) return _signingKeys;
            using var response = await httpClient.GetAsync($"{teamDomain}/cdn-cgi/access/certs", cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var keySet = new JsonWebKeySet(json);
            var allowedKeyIds = keySet.Keys
                .Where(key => key.Kty == "RSA" && (key.Alg is null || key.Alg == "RS256"))
                .Select(key => key.Kid).ToHashSet(StringComparer.Ordinal);
            var keys = keySet.GetSigningKeys()
                .Where(key => key is RsaSecurityKey && key.KeyId is not null && allowedKeyIds.Contains(key.KeyId))
                .ToArray();
            if (keys.Length == 0) throw new SecurityTokenException("Cloudflare Access returned no RSA signing keys.");
            _signingKeys = keys;
            _signingKeysExpireAt = DateTimeOffset.UtcNow.Add(SigningKeyCacheLifetime);
            return _signingKeys;
        }
        finally { _keyLock.Release(); }
    }
}

public sealed record CloudflareAccessSettings(string TeamDomain, string Hostname, string Audience, string[] AllowedEmails);
public sealed record CloudflareAccessIdentity(string Email);
