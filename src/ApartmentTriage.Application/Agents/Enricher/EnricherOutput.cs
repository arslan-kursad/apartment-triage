using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Enricher;

public sealed record EnricherOutput(
    float[]                      EmbeddingVector,
    IReadOnlyList<SimilarTicket> SimilarTickets,
    string?                      EnrichedContext,
    ConfidenceLevel              ConfidenceLevel
);
