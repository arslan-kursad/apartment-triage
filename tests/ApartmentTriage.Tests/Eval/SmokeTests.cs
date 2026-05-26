using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Application.Agents.Classifier;
using ApartmentTriage.Application.Agents.Router;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ApartmentTriage.Tests.Eval;

/// <summary>
/// Deploy gate smoke tests. Gerçek Haiku API kullanır; ANTHROPIC_API_KEY zorunlu.
/// DB gerektirmez — Classifier + Router agent katmanını doğrudan test eder.
///
/// ec-0003: typo "yanıkn" → Layer 1 phrase miss → LLM yakalamalı (IsEmergency=true)
/// ec-0007: "duman var" → Layer 1 hit → LLM confirm → TriggerEmergency (High/Med conf)
///                                                     EscalateToManager (Low conf, ADR-0009)
/// </summary>
public sealed class SmokeTests(ITestOutputHelper output)
{
    // ── Smoke: ec-0003 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0003_BurntSmellTypo_LlmShouldDetectEmergency()
    {
        var (classifier, router) = BuildAgents();

        // Layer 1: "yanıkn kokusu" → "yanık koku" phrase DOES NOT substring-match
        // due to the extra 'n'. EmergencySuspected=false, Layer 2 must detect.
        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "yanıkn kokusu var mutfaktan",
            ChannelType: ChannelType.WhatsApp,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0003] IsEmergency={co.IsEmergency} " +
                         $"EmergencyConf={co.EmergencyConfidence} " +
                         $"Category={co.Category} Severity={co.Severity}");

        co.IsEmergency.Should().BeTrue(
            "LLM (Layer 2) 'yanık kokusu' ifadesini emergency olarak tanımalı — " +
            "Layer 1 typo nedeniyle miss etse bile");

        // Router: IsEmergency=true → TriggerEmergency (High/Med) veya EscalateToManager (Low)
        var routerInput = BuildRouterInput(co, classifierInput.RawText);
        var routeResult = await router.ExecuteAsync(routerInput, ctx);

        routeResult.IsSuccess.Should().BeTrue("Router API call başarısız");
        var ro = routeResult.Value!;

        output.WriteLine($"[ec-0003] RoutingAction={ro.Action}");

        var expectedActions = new[] { RoutingAction.TriggerEmergency, RoutingAction.EscalateToManager };
        ro.Action.Should().BeOneOf(expectedActions,
            "IsEmergency=true → TriggerEmergency (High/Med conf) veya EscalateToManager (Low conf — ADR-0009)");
    }

    // ── Smoke: ec-0007 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0007_SmokeInCorridor_ShouldTriggerEmergencyOrEscalate()
    {
        var (classifier, router) = BuildAgents();

        // Layer 1: "duman var" → phrase match → EmergencySuspected=true
        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "biraz duman var koridordan",
            ChannelType: ChannelType.WhatsApp,
            EmergencySuspected: true,
            MatchedPhrases: ["duman var"]);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0007] IsEmergency={co.IsEmergency} " +
                         $"EmergencyConf={co.EmergencyConfidence} " +
                         $"Category={co.Category} Severity={co.Severity}");

        co.IsEmergency.Should().BeTrue(
            "LLM duman + EmergencySuspected=true sinyalini emergency olarak onaylamalı");

        var routerInput = BuildRouterInput(co, classifierInput.RawText);
        var routeResult = await router.ExecuteAsync(routerInput, ctx);

        routeResult.IsSuccess.Should().BeTrue("Router API call başarısız");
        var ro = routeResult.Value!;

        output.WriteLine($"[ec-0007] RoutingAction={ro.Action}");

        // ADR-0009: High/Med → TriggerEmergency, Low → EscalateToManager
        var expectedActions = new[] { RoutingAction.TriggerEmergency, RoutingAction.EscalateToManager };
        ro.Action.Should().BeOneOf(expectedActions,
            "IsEmergency=true → TriggerEmergency (High/Med) veya EscalateToManager (Low — ADR-0009 minimum guarantee)");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ClassifierAgent Classifier, RouterAgent Router) BuildAgents()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new SkipException("ANTHROPIC_API_KEY not set — smoke test skipped");

        var httpClient = BuildAnthropicHttpClient(apiKey);
        var factory = new SingletonHttpClientFactory(httpClient);
        var anthropicClient = new AnthropicClient(factory, NullLogger<AnthropicClient>.Instance);

        var classifier = new ClassifierAgent(
            anthropicClient,
            AnthropicModels.Haiku45,
            NullLogger<ClassifierAgent>.Instance);

        var router = new RouterAgent(
            anthropicClient,
            AnthropicModels.Haiku45,
            NullLogger<RouterAgent>.Instance);

        return (classifier, router);
    }

    private static AgentContext MakeContext() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static RouterInput BuildRouterInput(ClassifierOutput co, string rawText) =>
        new(
            TicketId: Guid.NewGuid(),
            Category: co.Category,
            Severity: co.Severity,
            CategoryConfidence: co.CategoryConfidence,
            IsEmergency: co.IsEmergency,
            EmergencyConfidence: co.EmergencyConfidence,
            SimilarTickets: [],
            AmbiguityReasons: co.AmbiguityReasons,
            RawText: rawText,
            ResidentId: Guid.NewGuid());

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

    private sealed class SingletonHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    /// <summary>xUnit'te native skip mekanizması yok — test exception ile sonlanır,
    /// CI'da Smoke trait filter'ı olmadan çalıştırıldığında görünür hale gelir.</summary>
    private sealed class SkipException(string message) : Exception(message);
}
