using ApartmentTriage.Application.Agents.Enricher;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Router;

public sealed record RouterInput(
    Guid                          TicketId,
    TicketCategory                Category,
    TicketSeverity                Severity,
    ConfidenceLevel               CategoryConfidence,
    bool                          IsEmergency,
    ConfidenceLevel               EmergencyConfidence,
    IReadOnlyList<SimilarTicket>  SimilarTickets,
    IReadOnlyList<AmbiguityReason> AmbiguityReasons,
    string                        RawText,
    Guid                          ResidentId
);
