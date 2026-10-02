using Microsoft.Extensions.DependencyInjection;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public static class SourceHttpClientRegistration
{
    public static IServiceCollection AddPublicKnowledgeSourceHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(PublicKnowledgeResearchService))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient(nameof(PublicKnowledgeSourceIndexService))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
