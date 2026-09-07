namespace PulseBrief;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (context.Request.Path.StartsWithSegments("/admin"))
        {
            context.Response.Headers["X-Robots-Tag"] = "noindex,nofollow";
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        }
        await next(context);
    }
}
