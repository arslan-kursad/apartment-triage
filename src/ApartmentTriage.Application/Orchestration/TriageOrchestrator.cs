using System.Text.Json;
using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Classifier;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Application.Orchestration;

/// <summary>
/// Drives the full triage pipeline: Classifier → orchestrator_rule → Ticket persistence.
/// Form B escalation: CategoryConfidence == Low → re-run with Sonnet variant.
/// taxonomy.v3.yaml § multi_issue.orchestrator_rule governs ticket-splitting logic.
/// </summary>
public sealed class TriageOrchestrator : ITriageOrchestrator
{
    private readonly IAgent<ClassifierInput, ClassifierOutput> _haikuClassifier;
    private readonly IAgent<ClassifierInput, ClassifierOutput> _sonnetClassifier;
    private readonly ITicketRepository _ticketRepository;
    private readonly ILogger<TriageOrchestrator> _logger;

    private const int MaxSwapDepth = 2; // taxonomy.v3.yaml § orchestrator_rule_constraints

    public TriageOrchestrator(
        [FromKeyedServices(AgentKeys.ClassifierHaiku)]
        IAgent<ClassifierInput, ClassifierOutput> haikuClassifier,
        [FromKeyedServices(AgentKeys.ClassifierSonnet)]
        IAgent<ClassifierInput, ClassifierOutput> sonnetClassifier,
        ITicketRepository ticketRepository,
        ILogger<TriageOrchestrator> logger)
    {
        _haikuClassifier = haikuClassifier;
        _sonnetClassifier = sonnetClassifier;
        _ticketRepository = ticketRepository;
        _logger = logger;
    }

    public async Task<TriageResult> ProcessAsync(Message message, CancellationToken cancellationToken = default)
    {
        var context = new AgentContext(
            CorrelationId: Guid.NewGuid(),
            MessageId: message.Id,
            ResidentId: message.ResidentId,
            ReceivedAt: new DateTimeOffset(message.ReceivedAt, TimeSpan.Zero));

        var input = new ClassifierInput(
            ResidentId: message.ResidentId,
            RawText: message.RawText,
            ChannelType: message.ChannelType,
            EmergencySuspected: message.EmergencySuspected,
            MatchedPhrases: Array.AsReadOnly(message.MatchedPhrases));

        // Primary classification attempt with Haiku
        var result = await _haikuClassifier.ExecuteAsync(input, context, cancellationToken);

        bool escalated = false;

        if (!result.IsSuccess)
            return TriageResult.Fail(result.Error!);

        var output = result.Value!;

        // Form B: quality-based escalation — low category confidence → Sonnet
        if (output.CategoryConfidence == ConfidenceLevel.Low)
        {
            _logger.LogInformation(
                "TriageOrchestrator: Form B escalation triggered for message {MessageId} (CategoryConfidence=Low)",
                message.Id);

            var escalationResult = await _sonnetClassifier.ExecuteAsync(input, context, cancellationToken);
            if (!escalationResult.IsSuccess)
            {
                _logger.LogWarning(
                    "TriageOrchestrator: Sonnet escalation failed for message {MessageId}, using Haiku result",
                    message.Id);
            }
            else
            {
                output = escalationResult.Value!;
                escalated = true;
            }
        }

        // Announcement → no ticket
        if (output.Category == TicketCategory.Announcement)
        {
            _logger.LogInformation(
                "TriageOrchestrator: message {MessageId} classified as Announcement, no ticket created",
                message.Id);
            return TriageResult.Ok([], escalated);
        }

        var secondaryIssuesJson = output.SecondaryIssues.Count > 0
            ? JsonSerializer.Serialize(output.SecondaryIssues)
            : null;

        var tickets = ApplyOrchestratorRule(message, output, secondaryIssuesJson);

        await _ticketRepository.AddRangeAsync(tickets, cancellationToken);
        await _ticketRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "TriageOrchestrator: created {Count} ticket(s) for message {MessageId} (escalated={Escalated}, ambiguity={AmbiguityCount})",
            tickets.Count, message.Id, escalated, output.AmbiguityReasons.Count);

        return TriageResult.Ok(tickets, escalated, output.AmbiguityReasons);
    }

    // Implements taxonomy.v3.yaml § multi_issue.orchestrator_rule
    // swapOrigins: secondaries that were the original primary before a cause_of_primary swap.
    // The effect_of_primary severity upgrade is NOT applied to swap-origin secondaries —
    // their severity was already factored in by the classifier when it evaluated the original primary.
    private List<Ticket> ApplyOrchestratorRule(
        Message message,
        ClassifierOutput output,
        string? secondaryIssuesJson,
        int swapDepth = 0,
        HashSet<ClassifierSecondaryIssue>? swapOrigins = null)
    {
        swapOrigins ??= [];

        if (output.SecondaryIssues.Count == 0)
            return [CreatePrimaryTicket(message, output, secondaryIssuesJson)];

        // cause_of_primary swap — re-evaluate with swapped state
        var causeOfPrimary = output.SecondaryIssues
            .FirstOrDefault(s => s.CausalRelation == CausalRelation.CauseOfPrimary);

        if (causeOfPrimary is not null)
        {
            if (swapDepth >= MaxSwapDepth)
            {
                _logger.LogWarning(
                    "TriageOrchestrator: max_swap_depth ({MaxSwapDepth}) reached for message {MessageId}. " +
                    "Proceeding with current state. Primary={Category}",
                    MaxSwapDepth, message.Id, output.Category);
                return [CreatePrimaryTicket(message, output, secondaryIssuesJson)];
            }

            var (swapped, swapOrigin) = SwapPrimaryWithCause(output, causeOfPrimary);
            var newSwapOrigins = new HashSet<ClassifierSecondaryIssue>(swapOrigins, ReferenceEqualityComparer.Instance) { swapOrigin };
            var newSecondaryJson = swapped.SecondaryIssues.Count > 0
                ? JsonSerializer.Serialize(swapped.SecondaryIssues)
                : null;
            return ApplyOrchestratorRule(message, swapped, newSecondaryJson, swapDepth + 1, newSwapOrigins);
        }

        // effect_of_primary → single ticket, severity upgrade if effect >= medium.
        // Swap-origin secondaries are excluded from the upgrade check (Architect decision 2026-05-19):
        // their severity was already evaluated by the classifier as the original primary.
        var effects = output.SecondaryIssues
            .Where(s => s.CausalRelation == CausalRelation.EffectOfPrimary && !swapOrigins.Contains(s))
            .ToList();

        if (effects.Count > 0)
        {
            var ticket = CreatePrimaryTicket(message, output, secondaryIssuesJson);

            var effectContext = string.Join("; ", effects.Select(e =>
                string.IsNullOrEmpty(e.Snippet) ? $"{e.Category}" : e.Snippet));
            ticket.SetContext(effectContext);

            if (effects.Any(e => e.Severity >= TicketSeverity.Medium))
                ticket.UpgradeSeverity();

            return [ticket];
        }

        // All secondary share primary category
        var independents = output.SecondaryIssues
            .Where(s => s.CausalRelation == CausalRelation.Independent)
            .ToList();

        if (independents.All(s => s.Category == output.Category))
        {
            // Same category — location-based split
            bool allSameLocation = independents.All(s => s.LocationHint == output.LocationHint);
            if (allSameLocation)
            {
                // Single ticket, secondaries in notes
                var ticket = CreatePrimaryTicket(message, output, secondaryIssuesJson);
                var notes = string.Join("; ", independents.Select(s =>
                    string.IsNullOrEmpty(s.Snippet) ? $"{s.Category}" : s.Snippet));
                ticket.SetNotes(notes);
                return [ticket];
            }

            // Distinct locations → separate ticket per location
            var tickets = new List<Ticket> { CreatePrimaryTicket(message, output, null) };
            foreach (var secondary in independents)
            {
                var secOutput = output with
                {
                    Category = secondary.Category,
                    Severity = secondary.Severity,
                    LocationHint = secondary.LocationHint,
                    SecondaryIssues = [],
                    Rationale = null
                };
                tickets.Add(CreatePrimaryTicket(message, secOutput, null));
            }
            return tickets;
        }

        // Different category, severity >= medium, independent → separate tickets
        var separableSecondaries = independents
            .Where(s => s.Category != output.Category && s.Severity >= TicketSeverity.Medium)
            .ToList();

        if (separableSecondaries.Count > 0)
        {
            var tickets = new List<Ticket> { CreatePrimaryTicket(message, output, secondaryIssuesJson) };
            foreach (var secondary in separableSecondaries)
            {
                var secOutput = output with
                {
                    Category = secondary.Category,
                    Severity = secondary.Severity,
                    LocationHint = secondary.LocationHint,
                    SecondaryIssues = [],
                    Rationale = null
                };
                tickets.Add(CreatePrimaryTicket(message, secOutput, null));
            }
            return tickets;
        }

        // Fallback: single ticket, all secondaries in notes
        var fallbackTicket = CreatePrimaryTicket(message, output, secondaryIssuesJson);
        var fallbackNotes = string.Join("; ", output.SecondaryIssues.Select(s =>
            string.IsNullOrEmpty(s.Snippet) ? $"{s.Category}" : s.Snippet));
        if (!string.IsNullOrEmpty(fallbackNotes))
            fallbackTicket.SetNotes(fallbackNotes);
        return [fallbackTicket];
    }

    // Swaps primary with cause_of_primary secondary per taxonomy spec.
    // Returns the swapped output AND the new secondary that represents the original primary,
    // so the caller can track it as a swap-origin and skip the effect upgrade rule for it.
    private static (ClassifierOutput Swapped, ClassifierSecondaryIssue SwapOrigin) SwapPrimaryWithCause(
        ClassifierOutput output, ClassifierSecondaryIssue cause)
    {
        var originalPrimaryAsEffect = new ClassifierSecondaryIssue(
            Category: output.Category,
            Severity: output.Severity,
            Snippet: output.Rationale ?? string.Empty,
            LocationHint: output.LocationHint,
            CausalRelation: CausalRelation.EffectOfPrimary);

        var newSecondaries = output.SecondaryIssues
            .Where(s => s != cause)
            .Append(originalPrimaryAsEffect)
            .ToList();

        var swapped = output with
        {
            Category = cause.Category,
            Severity = cause.Severity,
            LocationHint = cause.LocationHint,
            SecondaryIssues = newSecondaries
        };

        return (swapped, originalPrimaryAsEffect);
    }

    private static Ticket CreatePrimaryTicket(Message message, ClassifierOutput output, string? secondaryIssuesJson)
        => Ticket.Create(
            residentId: message.ResidentId,
            sourceMessageId: message.Id,
            category: output.Category,
            severity: output.Severity,
            categoryConfidence: output.CategoryConfidence,
            emergencyConfidence: output.EmergencyConfidence,
            locationHint: output.LocationHint,
            isEmergency: output.IsEmergency,
            emergencyConfirmedByLlm: output.IsEmergency,
            secondaryIssuesJson: secondaryIssuesJson);
}
