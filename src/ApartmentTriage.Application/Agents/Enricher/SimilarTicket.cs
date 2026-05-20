using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Enricher;

public sealed record SimilarTicket(
    Guid           TicketId,
    TicketCategory Category,
    float          CosineSimilarity,
    DateTime       CreatedAt
);
