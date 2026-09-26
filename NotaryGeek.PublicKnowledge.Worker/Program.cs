using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.Configure<OpenAiOptions>(context.Configuration.GetSection("OpenAI"));
        services.Configure<PublicKnowledgeOptions>(context.Configuration.GetSection("PublicKnowledge"));
        services.Configure<StraicoOptions>(context.Configuration.GetSection("Straico"));

        services.AddHttpClient();
        services.AddHttpClient(nameof(PublicKnowledgeResearchService))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient(nameof(PublicKnowledgeSourceIndexService))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<PublicKnowledgeResearchService>();
        services.AddSingleton<PublicKnowledgeSourceIndexService>();
        services.AddSingleton<PublicKnowledgeRunStorageService>();
        services.AddSingleton<PublicKnowledgeQueueService>();
        services.AddSingleton<PublicKnowledgePromotionService>();
    })
    .Build();

host.Run();
