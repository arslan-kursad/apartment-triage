using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Infrastructure.Anthropic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure;

public static class AnthropicClientExtensions
{
    public static IServiceCollection AddAnthropicClient(
        this IServiceCollection services,
        string apiKey,
        string baseUrl = "https://api.anthropic.com/v1/")
    {
        services.AddHttpClient("anthropic", client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            // Required header for prompt caching beta
            client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        // Real client + usage-recording decorator (self-instrumented FinOps).
        services.AddSingleton<AnthropicClient>();
        services.AddSingleton<IAnthropicClient>(sp => new UsageRecordingAnthropicClient(
            sp.GetRequiredService<AnthropicClient>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<UsageRecordingAnthropicClient>>()));

        return services;
    }
}
