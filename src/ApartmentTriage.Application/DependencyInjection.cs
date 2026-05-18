using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Application.Agents.Classifier;
using ApartmentTriage.Application.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentTriage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAgents();
        services.AddScoped<ITriageOrchestrator, TriageOrchestrator>();
        return services;
    }

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

        return services;
    }
}
