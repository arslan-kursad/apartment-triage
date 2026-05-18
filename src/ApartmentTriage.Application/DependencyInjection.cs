using Microsoft.Extensions.DependencyInjection;

namespace ApartmentTriage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAgents();
        return services;
    }

    // Separated from AddApplication so callers can compose selectively in tests.
    // Concrete IAgent<TIn, TOut> registrations are added here as agents are implemented.
    // Example: services.AddSingleton<IAgent<ClassifierInput, ClassifierOutput>, ClassifierAgent>();
    public static IServiceCollection AddAgents(this IServiceCollection services)
    {
        return services;
    }
}
