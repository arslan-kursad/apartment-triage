namespace ApartmentTriage.Domain.Entities;

/// <summary>
/// One Anthropic API call's token usage, captured from the live response (self-instrumented).
/// Tokens are the ground truth; USD cost is derived at read time from configurable rates,
/// so a pricing correction reprices history without a data migration.
/// </summary>
public sealed class ApiUsageRecord
{
    public Guid Id { get; private set; }

    /// <summary>Which agent made the call ("classifier" / "enricher" / "router"), or "system" if outside an agent.</summary>
    public string AgentId { get; private set; } = null!;

    /// <summary>Model string as sent to the API (e.g. "claude-haiku-4-5-20251001").</summary>
    public string Model { get; private set; } = null!;

    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int CacheReadTokens { get; private set; }
    public int CacheCreationTokens { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private ApiUsageRecord() { } // EF Core

    public static ApiUsageRecord Create(
        string agentId,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheReadTokens,
        int cacheCreationTokens)
    {
        return new ApiUsageRecord
        {
            Id = Guid.NewGuid(),
            AgentId = string.IsNullOrWhiteSpace(agentId) ? "system" : agentId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheCreationTokens = cacheCreationTokens,
            CreatedAt = DateTime.UtcNow
        };
    }
}
