using System.Reflection;
using System.Text.Json;
using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Application.Agents.Classifier;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApartmentTriage.Tests.Eval;

public sealed class ClassifierEvalTests
{
    [Fact, Trait("Category", "Eval")]
    public async Task Classifier_MeetsThresholds()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return; // filter-less run without key → silent skip; CI uses Trait filter

        using var httpClient = BuildAnthropicHttpClient(apiKey);
        var anthropicClient = new AnthropicClient(
            new SingletonHttpClientFactory(httpClient),
            NullLogger<AnthropicClient>.Instance);

        var classifier = new ClassifierAgent(
            anthropicClient,
            AnthropicModels.Haiku45,
            NullLogger<ClassifierAgent>.Instance);

        var cases = LoadCases();
        var results = new List<EvalResult>(cases.Count);

        foreach (var c in cases)
        {
            var input = new ClassifierInput(
                ResidentId: Guid.NewGuid(),
                RawText: c.Input.RawText,
                ChannelType: Enum.Parse<ChannelType>(c.Input.ChannelType, ignoreCase: true),
                EmergencySuspected: c.Input.EmergencySuspected,
                MatchedPhrases: c.Input.MatchedPhrases ?? []);

            var ctx = new AgentContext(
                CorrelationId: Guid.NewGuid(),
                MessageId: Guid.NewGuid(),
                ResidentId: input.ResidentId,
                ReceivedAt: DateTimeOffset.UtcNow);

            var result = await classifier.ExecuteAsync(input, ctx);

            TicketCategory actualCategory;
            bool actualIsEmergency;

            if (result.IsSuccess)
            {
                actualCategory = result.Value!.Category;
                actualIsEmergency = result.Value.IsEmergency;
            }
            else
            {
                actualCategory = TicketCategory.Other;
                actualIsEmergency = false;
            }

            results.Add(new EvalResult(
                CaseId: c.CaseId,
                ExpectedCategory: ParseCategory(c.Expected.Category),
                ActualCategory: actualCategory,
                ExpectedIsEmergency: c.Expected.IsEmergency,
                ActualIsEmergency: actualIsEmergency,
                Tags: c.Tags ?? []));
        }

        var score = EvalScoring.Calculate(results);
        score.Print();

        score.CategoryAccuracy.Should().BeGreaterThanOrEqualTo(0.80m,
            $"Category accuracy {score.CategoryAccuracy:P1} < 80%");
        score.EmergencyRecall.Should().BeGreaterThanOrEqualTo(0.95m,
            $"Emergency recall {score.EmergencyRecall:P1} < 95%");
        score.EmergencyPrecision.Should().BeGreaterThanOrEqualTo(0.70m,
            $"Emergency precision {score.EmergencyPrecision:P1} < 70%");
    }

    // ── Fixture loading ────────────────────────────────────────────────────────

    private static IReadOnlyList<EvalCase> LoadCases()
    {
        var assembly = typeof(ClassifierEvalTests).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(n => n.EndsWith("classifier_eval_cases.json", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var root = JsonSerializer.Deserialize<EvalCasesRoot>(stream, FixtureJsonOptions)!;
        return root.Cases;
    }

    private static readonly JsonSerializerOptions FixtureJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    // ── HTTP client factory shim ───────────────────────────────────────────────

    private static HttpClient BuildAnthropicHttpClient(string apiKey)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.anthropic.com/v1/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");
        return client;
    }

    private sealed class SingletonHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingletonHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    // ── Enum parsing ───────────────────────────────────────────────────────────

    private static readonly Dictionary<string, TicketCategory> CategoryMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["plumbing"]        = TicketCategory.Plumbing,
            ["electrical"]      = TicketCategory.Electrical,
            ["gas"]             = TicketCategory.Gas,
            ["heating_cooling"] = TicketCategory.HeatingCooling,
            ["elevator"]        = TicketCategory.Elevator,
            ["structural"]      = TicketCategory.Structural,
            ["common_area"]     = TicketCategory.CommonArea,
            ["pest"]            = TicketCategory.Pest,
            ["noise"]           = TicketCategory.Noise,
            ["neighbor_dispute"] = TicketCategory.NeighborDispute,
            ["billing"]         = TicketCategory.Billing,
            ["security"]        = TicketCategory.Security,
            ["announcement"]    = TicketCategory.Announcement,
            ["other"]           = TicketCategory.Other
        };

    private static TicketCategory ParseCategory(string value) =>
        CategoryMap.TryGetValue(value, out var cat)
            ? cat
            : throw new InvalidOperationException($"Unknown category in fixture: '{value}'");

    // ── Fixture DTOs ───────────────────────────────────────────────────────────

    private sealed record EvalCasesRoot(List<EvalCase> Cases);
    private sealed record EvalCase(string CaseId, EvalCaseInput Input, EvalCaseExpected Expected, List<string>? Tags);
    private sealed record EvalCaseInput(string RawText, string ChannelType, bool EmergencySuspected, List<string>? MatchedPhrases);
    private sealed record EvalCaseExpected(string Category, bool IsEmergency, string? Severity);
}
