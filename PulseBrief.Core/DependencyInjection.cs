namespace PulseBrief;

/// <summary>웹 서버와 수집기가 동일한 업무 처리 및 저장소 구성을 사용하도록 등록합니다.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPulseBriefCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<RssCollector>();
        services.AddSingleton<AppPaths>();
        services.AddSingleton<MongoArticleStore>();
        services.AddSingleton<IArticleStore>(provider => provider.GetRequiredService<MongoArticleStore>());
        services.AddHttpClient<ArticleContentFetcher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("ArticleContent:TimeoutSeconds", 15));
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<ArticleClusterer>();
        services.AddSingleton<BriefGenerator>();
        services.AddHttpClient<OpenAiDailySummaryClient>();
        services.AddSingleton<DailySummaryService>();
        services.AddSingleton<SummaryLinkService>();
        services.AddSingleton<PipelineRunTracker>();
        services.AddSingleton<OperationalLogService>();
        services.AddSingleton<OperationalDiagnosticsService>();
        services.AddSingleton<NewsPipeline>();
        return services;
    }
}
