using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Enricher;

public sealed record EnricherInput(
    Guid           TicketId,
    Guid           ResidentId,
    string         RawText,
    TicketCategory ClassifiedCategory
);
