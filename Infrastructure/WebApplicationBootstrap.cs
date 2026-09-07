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
        configure?.Invoke(builder);
        if (builder.Configuration.GetValue("Collector:EnableInWebHost", false))
        {
            builder.Services.AddHostedService<ScheduledRefreshService>();
        }

        var app = builder.Build();
        _ = app.Services.GetRequiredService<ApplicationLifetimeInfo>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
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
