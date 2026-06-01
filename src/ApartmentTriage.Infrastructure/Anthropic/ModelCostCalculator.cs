using ApartmentTriage.Application.Agents.Anthropic;
using Microsoft.Extensions.Configuration;

namespace ApartmentTriage.Infrastructure.Anthropic;

/// <summary>
/// Config-driven cost model. Rates live under <c>Anthropic:Pricing:&lt;family&gt;</c> (USD per
/// million tokens) and fall back to the public list prices below. Verify against
/// https://www.anthropic.com/pricing when models or rates change.
/// </summary>
public sealed class ModelCostCalculator(IConfiguration configuration) : IModelCostCalculator
{
    private sealed record Rates(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite);

    // Defaults: approximate public list prices (USD / million tokens).
    private static readonly IReadOnlyDictionary<string, Rates> Defaults = new Dictionary<string, Rates>
    {
        ["haiku"]  = new(1.00m, 5.00m, 0.10m, 1.25m),
        ["sonnet"] = new(3.00m, 15.00m, 0.30m, 3.75m),
        ["opus"]   = new(15.00m, 75.00m, 1.50m, 18.75m)
    };

    public decimal CostUsd(
        string model, long inputTokens, long outputTokens, long cacheReadTokens, long cacheCreationTokens)
    {
        var key = FamilyKey(model);
        if (key is null) return 0m;

        var d = Defaults[key];
        var section = configuration.GetSection($"Anthropic:Pricing:{key}");
        decimal Rate(string name, decimal fallback) => section.GetValue<decimal?>(name) ?? fallback;

        var perMillion =
            inputTokens         * Rate("InputPerMTok", d.Input) +
            outputTokens        * Rate("OutputPerMTok", d.Output) +
            cacheReadTokens     * Rate("CacheReadPerMTok", d.CacheRead) +
            cacheCreationTokens * Rate("CacheWritePerMTok", d.CacheWrite);

        return perMillion / 1_000_000m;
    }

    public string Family(string model) => FamilyKey(model) switch
    {
        "haiku"  => "Haiku",
        "sonnet" => "Sonnet",
        "opus"   => "Opus",
        _        => string.IsNullOrEmpty(model) ? "Unknown" : model
    };

    private static string? FamilyKey(string model)
    {
        if (string.IsNullOrEmpty(model)) return null;
        var m = model.ToLowerInvariant();
        if (m.Contains("haiku")) return "haiku";
        if (m.Contains("sonnet")) return "sonnet";
        if (m.Contains("opus")) return "opus";
        return null;
    }
}
