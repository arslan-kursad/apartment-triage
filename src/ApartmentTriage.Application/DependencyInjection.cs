using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Application.Agents.Classifier;
using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Application.Agents.Router;
using ApartmentTriage.Application.Embeddings;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAgents();
        services.AddScoped<ITriageOrchestrator, TriageOrchestrator>();
        return services;
    }

    // Injected by AddApplication above; exposed separately for test composition.
    // Caller is responsible for resolving IEmbeddingService and ITicketRepository from the container.

    // Separated from AddApplication so callers can compose selectively in tests.
    public static IServiceCollection AddAgents(this IServiceCollection services)
    {
        services.AddKeyedScoped<IAgent<ClassifierInput, ClassifierOutput>, ClassifierAgent>(
            AgentKeys.ClassifierHaiku,
            (sp, _) => new ClassifierAgent(
                sp.GetRequiredService<IAnthropicClient>(),
                AnthropicModels.Haiku45,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClassifierAgent>>()));

        services.AddKeyedScoped<IAgent<ClassifierInput, ClassifierOutput>, ClassifierAgent>(
            AgentKeys.ClassifierSonnet,
            (sp, _) => new ClassifierAgent(
                sp.GetRequiredService<IAnthropicClient>(),
                AnthropicModels.Sonnet46,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ClassifierAgent>>()));

        services.AddKeyedScoped<IAgent<RouterInput, RouterOutput>, RouterAgent>(
            AgentKeys.RouterHaiku,
            (sp, _) => new RouterAgent(
                sp.GetRequiredService<IAnthropicClient>(),
                AnthropicModels.Haiku45,
                sp.GetRequiredService<ILogger<RouterAgent>>()));

        services.AddKeyedScoped<IAgent<EnricherInput, EnricherOutput>, EnricherAgent>(
            AgentKeys.EnricherDefault,
            (sp, _) => new EnricherAgent(
                sp.GetRequiredService<IEmbeddingService>(),
                sp.GetRequiredService<ITicketRepository>(),
                sp.GetRequiredService<ILogger<EnricherAgent>>()));

        return services;
    }
}
