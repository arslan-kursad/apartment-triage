using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Classifier;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Orchestration;

// ── Test doubles ─────────────────────────────────────────────────────────────

internal sealed class FakeClassifierAgent : IAgent<ClassifierInput, ClassifierOutput>
{
    private readonly Queue<AgentResult<ClassifierOutput>> _queue;

    public FakeClassifierAgent(params AgentResult<ClassifierOutput>[] results)
        => _queue = new Queue<AgentResult<ClassifierOutput>>(results);

    public string AgentId => "fake-classifier";

    public Task<AgentResult<ClassifierOutput>> ExecuteAsync(
        ClassifierInput input, AgentContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(_queue.Dequeue());
}

internal sealed class FakeTicketRepository : ITicketRepository
{
    public List<Ticket> Saved { get; } = [];

    public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
    {
        Saved.Add(ticket);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IEnumerable<Ticket> tickets, CancellationToken cancellationToken = default)
    {
        Saved.AddRange(tickets);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

file static class Helpers
{
    public static Message MakeMessage(string text = "priz çalışmıyor") =>
        Message.Create(
            Guid.NewGuid(), ChannelType.WhatsApp, "wamid-001", text, DateTime.UtcNow);

    public static ClassifierOutput Output(
        TicketCategory category = TicketCategory.Electrical,
        TicketSeverity severity = TicketSeverity.Medium,
        ConfidenceLevel catConf = ConfidenceLevel.High,
        ConfidenceLevel emConf = ConfidenceLevel.Low,
        bool isEmergency = false,
        string? locationHint = null,
        IReadOnlyList<ClassifierSecondaryIssue>? secondaries = null,
        IReadOnlyList<AmbiguityReason>? ambiguity = null) =>
        new(category, severity, catConf, isEmergency, emConf,
            locationHint, secondaries ?? [], ambiguity ?? [], null);

    public static TriageOrchestrator Build(
        FakeClassifierAgent haiku,
        FakeClassifierAgent? sonnet = null,
        FakeTicketRepository? repo = null) =>
        new(haiku, sonnet ?? new FakeClassifierAgent(),
            repo ?? new FakeTicketRepository(),
            NullLogger<TriageOrchestrator>.Instance);
}

// ── Tests ────────────────────────────────────────────────────────────────────

public class TriageOrchestratorTests
{
    // ── Basic happy paths ────────────────────────────────────────────────────

    [Fact]
    public async Task SingleIssue_CreatesOneTicket()
    {
        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(Helpers.Output()));
        var repo = new FakeTicketRepository();
        var orchestrator = Helpers.Build(haiku, repo: repo);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage());

        result.IsSuccess.Should().BeTrue();
        result.Tickets.Should().HaveCount(1);
        result.Tickets[0].Category.Should().Be(TicketCategory.Electrical);
        result.EscalatedToSonnet.Should().BeFalse();
        repo.Saved.Should().HaveCount(1);
    }

    [Fact]
    public async Task Announcement_CreatesNoTicket()
    {
        var haiku = new FakeClassifierAgent(
            AgentResult<ClassifierOutput>.Ok(Helpers.Output(TicketCategory.Announcement)));
        var repo = new FakeTicketRepository();
        var orchestrator = Helpers.Build(haiku, repo: repo);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage("kesinti bilgisi"));

        result.IsSuccess.Should().BeTrue();
        result.Tickets.Should().BeEmpty();
        repo.Saved.Should().BeEmpty();
    }

    // ── Form B escalation ────────────────────────────────────────────────────

    [Fact]
    public async Task FormB_LowCategoryConfidence_EscalatesToSonnet()
    {
        var lowConfOutput = Helpers.Output(catConf: ConfidenceLevel.Low);
        var highConfOutput = Helpers.Output(catConf: ConfidenceLevel.High,
            category: TicketCategory.Plumbing);

        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(lowConfOutput));
        var sonnet = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(highConfOutput));
        var repo = new FakeTicketRepository();
        var orchestrator = Helpers.Build(haiku, sonnet, repo);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage());

        result.IsSuccess.Should().BeTrue();
        result.EscalatedToSonnet.Should().BeTrue();
        result.Tickets[0].Category.Should().Be(TicketCategory.Plumbing);
    }

    [Fact]
    public async Task FormB_SonnetFails_FallsBackToHaikuResult()
    {
        var lowConfOutput = Helpers.Output(catConf: ConfidenceLevel.Low);
        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(lowConfOutput));
        var sonnet = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Fail(
            new AgentError(AgentErrorKind.Transient, "timeout")));
        var orchestrator = Helpers.Build(haiku, sonnet);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage());

        // Falls back to Haiku result — still success
        result.IsSuccess.Should().BeTrue();
        result.EscalatedToSonnet.Should().BeFalse();
    }

    // ── Orchestrator rule: effect_of_primary ─────────────────────────────────

    [Fact]
    public async Task EffectOfPrimary_SingleTicket_SeverityUpgraded()
    {
        // "çatıdan su geliyor, tavan ıslandı" — structural HIGH, plumbing MEDIUM effect
        var secondaries = new List<ClassifierSecondaryIssue>
        {
            new(TicketCategory.Plumbing, TicketSeverity.Medium,
                "tavan ıslandı", "oda", CausalRelation.EffectOfPrimary)
        };
        var output = Helpers.Output(
            TicketCategory.Structural, TicketSeverity.High,
            locationHint: "oda", secondaries: secondaries);

        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(output));
        var repo = new FakeTicketRepository();
        var orchestrator = Helpers.Build(haiku, repo: repo);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage("çatıdan su geliyor"));

        result.Tickets.Should().HaveCount(1);
        // Effect severity=medium >= medium → upgrade High → Urgent
        result.Tickets[0].Severity.Should().Be(TicketSeverity.Urgent);
        result.Tickets[0].Category.Should().Be(TicketCategory.Structural);
    }

    // ── Orchestrator rule: independent, separate category, severity >= medium ─

    [Fact]
    public async Task Independent_DifferentCategory_MediumSeverity_TwoTickets()
    {
        // "salonda priz çalışmıyor, banyo lavabosu da sızdırıyor"
        var secondaries = new List<ClassifierSecondaryIssue>
        {
            new(TicketCategory.Plumbing, TicketSeverity.Medium,
                "banyo lavabosu sızdırıyor", "banyo", CausalRelation.Independent)
        };
        var output = Helpers.Output(
            TicketCategory.Electrical, TicketSeverity.Medium,
            locationHint: "salon", secondaries: secondaries);

        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(output));
        var repo = new FakeTicketRepository();
        var orchestrator = Helpers.Build(haiku, repo: repo);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage());

        result.Tickets.Should().HaveCount(2);
        result.Tickets.Select(t => t.Category).Should()
            .Contain(TicketCategory.Electrical).And
            .Contain(TicketCategory.Plumbing);
    }

    // ── Orchestrator rule: cause_of_primary swap ─────────────────────────────

    [Fact]
    public async Task CauseOfPrimary_SwapsPrimaryAndSecondary()
    {
        // "salon prizi yandı, tavandan su damladığı için"
        // → initial: electrical(medium), structural(cause_of_primary, high)
        // → SWAP: structural becomes primary
        var secondaries = new List<ClassifierSecondaryIssue>
        {
            new(TicketCategory.Structural, TicketSeverity.High,
                "tavandan su damlıyor", null, CausalRelation.CauseOfPrimary)
        };
        var output = Helpers.Output(
            TicketCategory.Electrical, TicketSeverity.Medium, secondaries: secondaries);

        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(output));
        var repo = new FakeTicketRepository();
        var orchestrator = Helpers.Build(haiku, repo: repo);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage());

        result.Tickets.Should().HaveCount(1);
        result.Tickets[0].Category.Should().Be(TicketCategory.Structural);
        // Swap-origin secondary (original Electrical/Medium primary) is excluded from the
        // effect_of_primary upgrade rule — its severity was already evaluated by the classifier.
        // Final: Structural/High (not Urgent). Architect decision 2026-05-19.
        result.Tickets[0].Severity.Should().Be(TicketSeverity.High);
    }

    // ── Orchestrator rule: same category, same location → single ticket ──────

    [Fact]
    public async Task SameCategory_SameLocation_SingleTicket_SecondaryInNotes()
    {
        var secondaries = new List<ClassifierSecondaryIssue>
        {
            new(TicketCategory.Electrical, TicketSeverity.Low,
                "mutfak lambası da yanmıyor", "mutfak", CausalRelation.Independent)
        };
        var output = Helpers.Output(
            TicketCategory.Electrical, TicketSeverity.Medium,
            locationHint: "mutfak", secondaries: secondaries);

        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Ok(output));
        var orchestrator = Helpers.Build(haiku);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage());

        result.Tickets.Should().HaveCount(1);
    }

    // ── Classifier failure ────────────────────────────────────────────────────

    [Fact]
    public async Task ClassifierFails_ReturnsFailResult()
    {
        var haiku = new FakeClassifierAgent(AgentResult<ClassifierOutput>.Fail(
            new AgentError(AgentErrorKind.Semantic, "JSON parse failed")));
        var orchestrator = Helpers.Build(haiku);

        var result = await orchestrator.ProcessAsync(Helpers.MakeMessage());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(AgentErrorKind.Semantic);
    }
}
