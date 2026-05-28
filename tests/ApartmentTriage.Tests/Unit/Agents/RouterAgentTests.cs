using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Application.Agents.Router;
using ApartmentTriage.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Agents;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class RouterHelpers
{
    public static RouterInput Build(
        TicketCategory   category          = TicketCategory.Plumbing,
        TicketSeverity   severity          = TicketSeverity.Medium,
        ConfidenceLevel  categoryConf      = ConfidenceLevel.High,
        bool             isEmergency       = false,
        ConfidenceLevel  emergencyConf     = ConfidenceLevel.Low,
        IReadOnlyList<SimilarTicket>?  similar    = null,
        IReadOnlyList<AmbiguityReason>? ambiguity = null,
        string           rawText           = "test mesajı") =>
        new(
            TicketId:            Guid.NewGuid(),
            Category:            category,
            Severity:            severity,
            CategoryConfidence:  categoryConf,
            IsEmergency:         isEmergency,
            EmergencyConfidence: emergencyConf,
            SimilarTickets:      similar   ?? [],
            AmbiguityReasons:    ambiguity ?? [],
            RawText:             rawText,
            ResidentId:          Guid.NewGuid());

    public static AgentContext Ctx() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    public static RouterAgent Agent(IAnthropicClient? client = null) =>
        new(client ?? new NeverCalledClient(), AnthropicModels.Haiku45,
            NullLogger<RouterAgent>.Instance);

    public static SimilarTicket Similar(float similarity) =>
        new(Guid.NewGuid(), TicketCategory.Plumbing, similarity, DateTime.UtcNow);
}

// Fails the test if called — ensures LLM is not invoked in rule-based paths.
file sealed class NeverCalledClient : IAnthropicClient
{
    public Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("LLM should not be called in this test path");
}

// Queued fake for LLM fallback tests.
file sealed class QueuedClient : IAnthropicClient
{
    private readonly Queue<string> _queue;
    public QueuedClient(params string[] responses)
        => _queue = new Queue<string>(responses);

    public Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AnthropicResponse(
            _queue.Dequeue(), "end_turn", new AnthropicUsage(100, 50, 0, 100)));
}

// ── Layer 1: Emergency Fast-Path ──────────────────────────────────────────────

public class RouterAgent_Layer1_EmergencyTests
{
    [Theory]
    [InlineData(ConfidenceLevel.High)]
    [InlineData(ConfidenceLevel.Medium)]
    public async Task IsEmergency_HighOrMediumConfidence_TriggerEmergency_NoLlm(ConfidenceLevel conf)
    {
        var input = RouterHelpers.Build(isEmergency: true, emergencyConf: conf);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.TriggerEmergency);
        result.Value.ConfidenceLevel.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public async Task IsEmergency_LowConfidence_EscalatesToManager_NoLlm()
    {
        // Low confidence emergency → Layer 1 does NOT fire (only High/Medium does).
        // Layer 2 Rule 1 fires: minimum safety guarantee → EscalateToManager.
        // LLM must NOT be called (NeverCalledClient enforces this).
        var input = RouterHelpers.Build(isEmergency: true, emergencyConf: ConfidenceLevel.Low);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.EscalateToManager);
        result.Value.ConfidenceLevel.Should().Be(ConfidenceLevel.Medium);
        result.Value.NotificationNote.Should().NotBeNullOrEmpty();
    }
}

// ── Layer 2: Rule-Based ───────────────────────────────────────────────────────

public class RouterAgent_Layer2_RuleTests
{
    [Fact]
    public async Task Severity_Urgent_EscalatesToManager()
    {
        var input = RouterHelpers.Build(severity: TicketSeverity.Urgent);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.EscalateToManager);
        result.Value.ConfidenceLevel.Should().Be(ConfidenceLevel.High);
    }

    [Fact]
    public async Task AmbiguityReason_NonActionable_Archives()
    {
        var input = RouterHelpers.Build(
            ambiguity: [AmbiguityReason.NonActionable]);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.Archive);
    }

    [Theory]
    [InlineData(AmbiguityReason.MissingLocation)]
    [InlineData(AmbiguityReason.MissingSeverity)]
    [InlineData(AmbiguityReason.CategoryAmbiguous)]
    [InlineData(AmbiguityReason.LanguageUnclear)]
    [InlineData(AmbiguityReason.NeedsVisual)]
    public async Task AmbiguityReasons_NonEmpty_NotifiesResident(AmbiguityReason reason)
    {
        var input = RouterHelpers.Build(ambiguity: [reason]);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.NotifyResident);
        result.Value.NotificationNote.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SimilarTicket_AboveThreshold_AssignsTechnician()
    {
        var input = RouterHelpers.Build(
            similar: [RouterHelpers.Similar(0.91f)]);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.AssignTechnician);
    }

    [Fact]
    public async Task SimilarTicket_BelowThreshold_FallsThroughToLlm()
    {
        var llm = new QueuedClient("""{"action":"assign_technician"}""");
        var input = RouterHelpers.Build(similar: [RouterHelpers.Similar(0.89f)]);
        var agent = new RouterAgent(llm, AnthropicModels.Haiku45, NullLogger<RouterAgent>.Instance);

        var result = await agent.ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        // action comes from LLM, not rule
        result.Value!.ConfidenceLevel.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public async Task Rule_Priority_NonActionable_BeforeUrgent()
    {
        // Rule ordering: NonActionable (Rule 2) fires before Urgent (Rule 3).
        // When both conditions are present, Archive wins over EscalateToManager.
        var input = RouterHelpers.Build(
            severity: TicketSeverity.Urgent,
            ambiguity: [AmbiguityReason.NonActionable]);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.Value!.Action.Should().Be(RoutingAction.Archive);
    }

    [Fact]
    public async Task Rule_Priority_LowConfidenceEmergency_BeforeNonActionable()
    {
        // Rule 1 (emergency minimum guarantee) fires before Rule 2 (NonActionable).
        var input = RouterHelpers.Build(
            isEmergency: true,
            emergencyConf: ConfidenceLevel.Low,
            ambiguity: [AmbiguityReason.NonActionable]);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.Value!.Action.Should().Be(RoutingAction.EscalateToManager);
    }
}

// ── Layer 3: LLM Fallback ─────────────────────────────────────────────────────

public class RouterAgent_Layer3_LlmTests
{
    [Theory]
    [InlineData("""{"action":"assign_technician"}""",   RoutingAction.AssignTechnician)]
    [InlineData("""{"action":"escalate_to_manager"}""", RoutingAction.EscalateToManager)]
    [InlineData("""{"action":"defer"}""",               RoutingAction.Defer)]
    public async Task LlmResponse_ValidAction_MapsCorrectly(string llmJson, RoutingAction expected)
    {
        var input = RouterHelpers.Build(); // Medium severity, no ambiguity, no similar → LLM
        var agent = new RouterAgent(
            new QueuedClient(llmJson), AnthropicModels.Haiku45,
            NullLogger<RouterAgent>.Instance);

        var result = await agent.ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(expected);
        result.Value.ConfidenceLevel.Should().Be(ConfidenceLevel.Medium);
    }

    [Fact]
    public async Task LlmResponse_DisallowedAction_DefaultsToAssignTechnician()
    {
        // LLM returns TriggerEmergency which is not a valid LLM action.
        var input = RouterHelpers.Build();
        var agent = new RouterAgent(
            new QueuedClient("""{"action":"trigger_emergency"}"""), AnthropicModels.Haiku45,
            NullLogger<RouterAgent>.Instance);

        var result = await agent.ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.AssignTechnician);
    }

    [Fact]
    public async Task LlmResponse_InvalidJson_DefaultsToAssignTechnician_LowConfidence()
    {
        var input = RouterHelpers.Build();
        var agent = new RouterAgent(
            new QueuedClient("not json at all"), AnthropicModels.Haiku45,
            NullLogger<RouterAgent>.Instance);

        var result = await agent.ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.AssignTechnician);
        result.Value.ConfidenceLevel.Should().Be(ConfidenceLevel.Low);
    }

    // ── ec-0034 — Urgent + MissingLocation → EscalateToManager with location note ─

    [Fact]
    public async Task Ec0034_Rule3_Urgent_WithMissingLocation_IncludesLocationNote()
    {
        var input = RouterHelpers.Build(
            severity: TicketSeverity.Urgent,
            ambiguity: [AmbiguityReason.MissingLocation]);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.EscalateToManager,
            "Urgent → Rule 3 fires regardless of missing location");
        result.Value.NotificationNote.Should().NotBeNullOrEmpty(
            "Manager must be informed that location is unknown so they can follow up");
    }

    [Fact]
    public async Task Ec0034_Rule3_Urgent_WithoutMissingLocation_HasNullNote()
    {
        var input = RouterHelpers.Build(
            severity: TicketSeverity.Urgent,
            ambiguity: []);
        var result = await RouterHelpers.Agent().ExecuteAsync(input, RouterHelpers.Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be(RoutingAction.EscalateToManager);
        result.Value.NotificationNote.Should().BeNull(
            "No location ambiguity — note should not be added unnecessarily");
    }
}
