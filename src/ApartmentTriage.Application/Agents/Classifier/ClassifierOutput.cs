using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Classifier;

public sealed record ClassifierOutput(
    TicketCategory Category,
    TicketSeverity Severity,
    ConfidenceLevel CategoryConfidence,
    bool IsEmergency,
    ConfidenceLevel EmergencyConfidence,
    string? LocationHint,
    IReadOnlyList<ClassifierSecondaryIssue> SecondaryIssues,
    IReadOnlyList<AmbiguityReason> AmbiguityReasons,
    string? Rationale);
