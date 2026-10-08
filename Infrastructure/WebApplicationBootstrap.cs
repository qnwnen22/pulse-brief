using System.Text.Json;
using System.Text.Json.Serialization;
using PulseBrief.Controllers;

namespace PulseBrief;

/// <summary>HTTP 설정, Controller 및 업무 서비스를 연결하는 웹 애플리케이션 시작점입니다.</summary>
public static class WebApplicationBootstrap
{
    public static WebApplication Build(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);
        DotEnv.Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));

        builder.Services.AddControllers(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        }).AddApplicationPart(typeof(HealthController).Assembly).AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions));
        builder.Services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));
        builder.Services.AddPulseBriefCore(builder.Configuration);
        builder.Services.AddSingleton<AdminAuthService>();
        builder.Services.AddSingleton<ApplicationLifetimeInfo>();
        builder.Services.AddSingleton<NewsQueryService>();
        builder.Services.AddSingleton<AdminContentService>();
        builder.Services.AddSingleton<AdminFeedService>();
        builder.Services.AddSingleton<ArticleMaintenanceService>();
        builder.Services.AddHttpClient("cloudflare-access-jwks", client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton(provider => new CloudflareAccessJwtAuthenticator(
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("cloudflare-access-jwks")));
        configure?.Invoke(builder);
        if (builder.Configuration.GetValue("Collector:EnableInWebHost", false))
        {
            builder.Services.AddHostedService<ScheduledRefreshService>();
        }

        var app = builder.Build();
        _ = app.Services.GetRequiredService<ApplicationLifetimeInfo>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        var manualSummaryMcpEnabled = CloudflareAccessJwtAuthenticator.IsEnabled(app.Configuration);
        if (manualSummaryMcpEnabled)
            ManualSummaryMcpEndpoint.Map(app, CloudflareAccessJwtAuthenticator.ReadSettings(app.Configuration));
        else
            app.Use(async (context, next) =>
            {
                if (string.Equals(context.Request.Path.Value, "/mcp", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await next(context);
            });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();
        app.MapFallbackToFile("index.html");
        return app;
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }
}
