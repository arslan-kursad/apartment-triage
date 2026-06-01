using ApartmentTriage.Infrastructure.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Anthropic;

public class ModelCostCalculatorTests
{
    private static ModelCostCalculator WithConfig(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        return new ModelCostCalculator(config);
    }

    [Fact]
    public void Haiku_DefaultRates_PricesInputAndOutput()
    {
        var calc = WithConfig();   // defaults: Haiku $1 in / $5 out per MTok

        var cost = calc.CostUsd("claude-haiku-4-5-20251001", 1_000_000, 1_000_000, 0, 0);

        cost.Should().Be(6.00m);   // $1 + $5
    }

    [Fact]
    public void Sonnet_DefaultRates_PricesInputAndOutput()
    {
        var calc = WithConfig();   // defaults: Sonnet $3 in / $15 out per MTok

        var cost = calc.CostUsd("claude-sonnet-4-6", 1_000_000, 1_000_000, 0, 0);

        cost.Should().Be(18.00m);  // $3 + $15
    }

    [Fact]
    public void Haiku_CacheTokens_PricedSeparately()
    {
        var calc = WithConfig();   // cache read $0.10, cache write $1.25 per MTok

        var cost = calc.CostUsd("claude-haiku-4-5", 0, 0, 1_000_000, 1_000_000);

        cost.Should().Be(1.35m);   // $0.10 + $1.25
    }

    [Fact]
    public void UnknownModel_CostsZero()
        => WithConfig().CostUsd("gpt-4o", 1_000_000, 1_000_000, 0, 0).Should().Be(0m);

    [Fact]
    public void ConfiguredRate_OverridesDefault()
    {
        var calc = WithConfig(("Anthropic:Pricing:haiku:InputPerMTok", "2.00"));

        var cost = calc.CostUsd("claude-haiku-4-5", 1_000_000, 0, 0, 0);

        cost.Should().Be(2.00m);
    }

    [Theory]
    [InlineData("claude-haiku-4-5-20251001", "Haiku")]
    [InlineData("claude-sonnet-4-6", "Sonnet")]
    [InlineData("gpt-4o", "gpt-4o")]
    public void Family_NormalizesKnownModels(string model, string expected)
        => WithConfig().Family(model).Should().Be(expected);
}
