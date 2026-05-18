using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Classifier;

public sealed record ClassifierSecondaryIssue(
    TicketCategory Category,
    TicketSeverity Severity,
    string Snippet,
    string? LocationHint,
    CausalRelation CausalRelation);
