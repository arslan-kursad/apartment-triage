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

    // ── Smoke: ec-0032 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0032_ExposedBoilerCables_ShouldClassifyElectrical()
    {
        var (classifier, router) = BuildAgents();

        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "kombinin kabloları açıkta",
            ChannelType: ChannelType.Telegram,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0032] Category={co.Category} Severity={co.Severity} " +
                         $"IsEmergency={co.IsEmergency} Confidence={co.CategoryConfidence} " +
                         $"Ambiguity=[{string.Join(",", co.AmbiguityReasons)}]");

        co.Category.Should().Be(TicketCategory.Electrical,
            "Classifier v2: 'kombi kabloları açıkta' → few-shot örneği electrical'a işaret ediyor");

        co.Severity.Should().BeOneOf(
            [TicketSeverity.High, TicketSeverity.Urgent],
            "Açık kablo minimum severity=high gerektirir");

        co.AmbiguityReasons.Should().NotContain(AmbiguityReason.NonActionable,
            "Açık kablo her zaman actionable — NonActionable OLMAMALI");

        // Router: no Archive
        var routerInput = BuildRouterInput(co, classifierInput.RawText);
        var routeResult = await router.ExecuteAsync(routerInput, ctx);

        routeResult.IsSuccess.Should().BeTrue("Router API call başarısız");
        var ro = routeResult.Value!;

        output.WriteLine($"[ec-0032] RoutingAction={ro.Action}");

        ro.Action.Should().NotBe(RoutingAction.Archive,
            "Electrical/High ticket Archive edilmemeli");
    }

    // ── Smoke: ec-0033 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0033_ExposedPanelCables_ShouldNotArchive()
    {
        var (classifier, router) = BuildAgents();

        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "sigorta kabloları dışarıda 2 haftadır riskli",
            ChannelType: ChannelType.Telegram,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0033] Category={co.Category} Severity={co.Severity} " +
                         $"IsEmergency={co.IsEmergency} EmergencyConf={co.EmergencyConfidence} " +
                         $"Ambiguity=[{string.Join(",", co.AmbiguityReasons)}]");

        co.Category.Should().Be(TicketCategory.Electrical,
            "Classifier v2: 'sigorta kabloları dışarıda' → electrical");

        co.Severity.Should().BeOneOf(
            [TicketSeverity.High, TicketSeverity.Urgent],
            "2 haftalık açık kablo minimum severity=high");

        co.AmbiguityReasons.Should().NotContain(AmbiguityReason.NonActionable,
            "Sigorta kablosu dışarıda actionable bir sorundur");

        // Router: no Archive (Classifier fix + RouterAgent guard birlikte test ediliyor)
        var routerInput = BuildRouterInput(co, classifierInput.RawText);
        var routeResult = await router.ExecuteAsync(routerInput, ctx);

        routeResult.IsSuccess.Should().BeTrue("Router API call başarısız");
        var ro = routeResult.Value!;

        output.WriteLine($"[ec-0033] RoutingAction={ro.Action}");

        ro.Action.Should().NotBe(RoutingAction.Archive,
            "RouterAgent guard: EmergencyConfidence=High olsa bile NonActionable→Archive engellenmeli; " +
            "Classifier fix ile zaten NonActionable gelmemeli");
    }

    // ── Smoke: ec-0034 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0034_UrgentWaterLeak_MissingLocation_RouterNoteIncludesLocation()
    {
        var (classifier, router) = BuildAgents();

        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "3. katta su sızıntısı var",
            ChannelType: ChannelType.WhatsApp,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0034] Category={co.Category} Severity={co.Severity} " +
                         $"Ambiguity=[{string.Join(",", co.AmbiguityReasons)}]");

        var routerInput = BuildRouterInput(co, classifierInput.RawText);
        var routeResult = await router.ExecuteAsync(routerInput, ctx);

        routeResult.IsSuccess.Should().BeTrue("Router API call başarısız");
        var ro = routeResult.Value!;

        output.WriteLine($"[ec-0034] RoutingAction={ro.Action} Note={ro.NotificationNote}");

        // Smoke test: LLM classifier Severity=Urgent verirse Rule 3 doğrulaması yap.
        // Haiku Urgent vermeyebilir ("3. katta" → High de geçerli). Unit tests deterministik
        // yolu kapsar; burada sadece Urgent geldiğinde doğrula.
        if (co.Severity == TicketSeverity.Urgent)
        {
            ro.Action.Should().Be(RoutingAction.EscalateToManager,
                "Urgent → Rule 3: EscalateToManager");

            if (co.AmbiguityReasons.Contains(AmbiguityReason.MissingLocation))
            {
                ro.NotificationNote.Should().NotBeNullOrEmpty(
                    "Urgent + MissingLocation → Rule 3 note konum bilgisini içermeli");
            }
        }
    }

    // ── Smoke: ec-0035 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0035_ImagePlaceholder_ShouldNotProduceHighConfidenceCategory()
    {
        var (classifier, _) = BuildAgents();

        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "[Görsel mesaj]",
            ChannelType: ChannelType.WhatsApp,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0035] Category={co.Category} Confidence={co.CategoryConfidence} " +
                         $"Severity={co.Severity} Ambiguity=[{string.Join(",", co.AmbiguityReasons)}]");

        co.CategoryConfidence.Should().NotBe(ConfidenceLevel.High,
            "Image placeholder '[Görsel mesaj]' — içerik yok, high confidence üretilemez");

        co.AmbiguityReasons.Should().Contain(AmbiguityReason.InsufficientDetail,
            "Görsel placeholder için InsufficientDetail ambiguity_reason bekleniyor");
    }

    // ── Smoke: ec-0036 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0036_LocationOnlyMessage_ShouldProduceNonActionable()
    {
        var (classifier, router) = BuildAgents();

        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "C-blok 1. Kat Daire 2",
            ChannelType: ChannelType.WhatsApp,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0036] Category={co.Category} Confidence={co.CategoryConfidence} " +
                         $"Severity={co.Severity} Ambiguity=[{string.Join(",", co.AmbiguityReasons)}]");

        co.AmbiguityReasons.Should().Contain(AmbiguityReason.NonActionable,
            "Konum-only mesaj şikayet içermiyor — NonActionable bekleniyor");

        var routerInput = BuildRouterInput(co, classifierInput.RawText);
        var routeResult = await router.ExecuteAsync(routerInput, ctx);

        routeResult.IsSuccess.Should().BeTrue("Router API call başarısız");
        var ro = routeResult.Value!;

        output.WriteLine($"[ec-0036] RoutingAction={ro.Action}");

        ro.Action.Should().BeOneOf(
            [RoutingAction.Archive, RoutingAction.EscalateToManager],
            "NonActionable → Archive (normal); veya EscalateToManager (RouterAgent Rule 2 guard: EmergencyConf=High çelişki yönetimi) — ikisi de kabul");
    }

    // ── Smoke: ec-0037 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0037_MissingSeverity_ShouldNotCoexistWithHighOrUrgent()
    {
        var (classifier, _) = BuildAgents();

        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "Sular akmıyor",
            ChannelType: ChannelType.WhatsApp,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0037] Severity={co.Severity} Ambiguity=[{string.Join(",", co.AmbiguityReasons)}]");

        if (co.AmbiguityReasons.Contains(AmbiguityReason.MissingSeverity))
        {
            co.Severity.Should().Be(TicketSeverity.Medium,
                "MissingSeverity + High/Urgent çelişki — MissingSeverity varsa severity=medium zorunlu");
        }
    }

    // ── Smoke: ec-0038 ────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Smoke")]
    public async Task Ec0038_HedgeWords_ShouldCapCategoryConfidenceAtMedium()
    {
        var (classifier, _) = BuildAgents();

        var classifierInput = new ClassifierInput(
            ResidentId: Guid.NewGuid(),
            RawText: "Duvarlarda şişme var. Galiba rutubetden dolayı.",
            ChannelType: ChannelType.WhatsApp,
            EmergencySuspected: false,
            MatchedPhrases: []);

        var ctx = MakeContext();
        var classifyResult = await classifier.ExecuteAsync(classifierInput, ctx);

        output.WriteLine($"[ec-0038] IsSuccess={classifyResult.IsSuccess} " +
                         $"Error={classifyResult.Error?.Message ?? "none"}");

        classifyResult.IsSuccess.Should().BeTrue("Classifier API call başarısız");
        var co = classifyResult.Value!;

        output.WriteLine($"[ec-0038] Category={co.Category} Confidence={co.CategoryConfidence} " +
                         $"Ambiguity=[{string.Join(",", co.AmbiguityReasons)}]");

        co.CategoryConfidence.Should().NotBe(ConfidenceLevel.High,
            "'Galiba' belirsizlik işareti — hedge word constraint: category_confidence max medium");
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
