using Microsoft.AspNetCore.Mvc;

namespace PulseBrief.Controllers;

[ApiController]
public sealed class AdminSessionController(
    IWebHostEnvironment environment,
    AdminAuthService adminAuth) : ApiControllerBase
{
    [HttpGet("/admin")]
    public IResult Index()
    {
        return Results.File(AdminIndexPath(environment), "text/html");
    }

    [HttpPost("/api/admin/login")]
    public IResult Login([FromBody] AdminLoginRequest request)
    {
        var result = adminAuth.SignIn(HttpContext, request.Token);
        return result is null ? AdminAuthService.AdminRequired() : Results.Ok(result);
    }

    [HttpGet("/api/admin/session")]
    public IResult Session()
    {
        if (!adminAuth.IsAuthenticated(HttpContext)) return Results.Ok(new { authenticated = false });

        return Results.Ok(new
        {
            authenticated = true,
            csrfToken = adminAuth.CreateCsrfToken(HttpContext)
        });
    }

    [HttpPost("/api/admin/logout")]
    public IResult Logout()
    {
        if (!RequireAdmin(HttpContext, adminAuth, requireCsrf: true, out var denied)) return denied;

        adminAuth.SignOut(HttpContext);
        return Results.Ok(new { ok = true });
    }
}
