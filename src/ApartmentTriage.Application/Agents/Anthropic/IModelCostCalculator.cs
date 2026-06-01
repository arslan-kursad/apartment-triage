namespace ApartmentTriage.Application.Agents.Anthropic;

/// <summary>
/// Derives USD cost from token usage using configurable per-million-token rates.
/// Cost is computed at read time (not stored), so a rate correction reprices all history.
/// </summary>
public interface IModelCostCalculator
{
    decimal CostUsd(
        string model,
        long inputTokens,
        long outputTokens,
        long cacheReadTokens,
        long cacheCreationTokens);

    /// <summary>Display family for grouping ("Haiku", "Sonnet", "Opus", or the raw model string).</summary>
    string Family(string model);
}
