using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure.Anthropic;

/// <summary>
/// Decorator that records each call's token usage (self-instrumented FinOps source) after
/// delegating to the real client. Attribution comes from <see cref="AgentExecutionContext"/>.
/// Recording failures are swallowed — instrumentation must never break a triage call.
/// </summary>
public sealed class UsageRecordingAnthropicClient(
    IAnthropicClient inner,
    IServiceScopeFactory scopeFactory,
    ILogger<UsageRecordingAnthropicClient> logger) : IAnthropicClient
{
    public async Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request, CancellationToken cancellationToken = default)
    {
        var response = await inner.CompleteAsync(request, cancellationToken);
        await TryRecordAsync(request.Model, response.Usage, cancellationToken);
        return response;
    }

    private async Task TryRecordAsync(string model, AnthropicUsage usage, CancellationToken ct)
    {
        try
        {
            var agentId = AgentExecutionContext.CurrentAgentId ?? "system";

            // Singleton decorator → open a scope to reach the scoped DbContext.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApartmentTriageDbContext>();

            db.ApiUsageRecords.Add(ApiUsageRecord.Create(
                agentId: agentId,
                model: model,
                inputTokens: usage.InputTokens,
                outputTokens: usage.OutputTokens,
                cacheReadTokens: usage.CacheReadInputTokens,
                cacheCreationTokens: usage.CacheCreationInputTokens));

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record API usage for model {Model} — triage unaffected", model);
        }
    }
}
