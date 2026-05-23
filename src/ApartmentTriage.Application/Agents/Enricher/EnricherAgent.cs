using ApartmentTriage.Application.Embeddings;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Application.Agents.Enricher;

public sealed class EnricherAgent : AgentBase<EnricherInput, EnricherOutput>
{
    private readonly IEmbeddingService _embeddings;
    private readonly ITicketRepository _tickets;
    private readonly int _topK;

    public EnricherAgent(
        IEmbeddingService embeddings,
        ITicketRepository tickets,
        ILogger<EnricherAgent> logger,
        int topK = 5)
        : base(logger)
    {
        _embeddings = embeddings;
        _tickets = tickets;
        _topK = topK;
    }

    public override string AgentId => "enricher/onnx-pgvector";

    protected override bool ShouldEscalate(AgentError error, int attempt) => false;

    protected override async Task<AgentResult<EnricherOutput>> ExecuteCoreAsync(
        EnricherInput input, AgentContext context, CancellationToken cancellationToken)
    {
        // Fix 2: ONNX undefined behavior on empty string (zero-vector or exception).
        // Skip embedding, return empty result — Router handles Low confidence gracefully.
        if (string.IsNullOrWhiteSpace(input.RawText))
        {
            Logger.LogWarning(
                "EnricherAgent [{TicketId}]: empty RawText — skipping embedding, " +
                "returning empty SimilarTickets",
                input.TicketId);
            return AgentResult<EnricherOutput>.Ok(
                new EnricherOutput(
                    EmbeddingVector: [],
                    SimilarTickets: [],
                    EnrichedContext: null,
                    ConfidenceLevel: ConfidenceLevel.Low));
        }

        float[] vector;
        try
        {
            vector = await _embeddings.GetEmbeddingAsync(input.RawText, cancellationToken);
            if (vector.Length == 0)
            {
                Logger.LogWarning(
                    "EnricherAgent: embedding unavailable for ticket {TicketId} — skipping similarity search",
                    input.TicketId);

                return AgentResult<EnricherOutput>.Ok(new EnricherOutput(
                    EmbeddingVector: [],
                    SimilarTickets: [],
                    EnrichedContext: null,
                    ConfidenceLevel: ConfidenceLevel.Low));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "EnricherAgent: embedding generation failed for ticket {TicketId}", input.TicketId);
            return AgentResult<EnricherOutput>.Fail(new AgentError(
                AgentErrorKind.Transient, $"Embedding generation failed: {ex.Message}", ex));
        }

        IReadOnlyList<SimilarTicket> similar;
        try
        {
            similar = await _tickets.FindSimilarAsync(
                vector, input.TicketId, _topK, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "EnricherAgent: similarity search failed for ticket {TicketId}", input.TicketId);
            return AgentResult<EnricherOutput>.Fail(new AgentError(
                AgentErrorKind.Transient, $"Similarity search failed: {ex.Message}", ex));
        }

        var confidence = ComputeConfidence(similar);

        Logger.LogInformation(
            "EnricherAgent: ticket {TicketId} — {SimilarCount} similar tickets, confidence {Confidence}",
            input.TicketId, similar.Count, confidence);

        return AgentResult<EnricherOutput>.Ok(new EnricherOutput(
            EmbeddingVector: vector,
            SimilarTickets: similar,
            EnrichedContext: null,  // search-only mode; LLM enrichment deferred to Day 9+
            ConfidenceLevel: confidence));
    }

    private static ConfidenceLevel ComputeConfidence(IReadOnlyList<SimilarTicket> similar)
    {
        if (similar.Count == 0)
            return ConfidenceLevel.Low;

        var top = similar[0].CosineSimilarity;
        return top switch
        {
            > 0.85f => ConfidenceLevel.High,
            > 0.65f => ConfidenceLevel.Medium,
            _       => ConfidenceLevel.Low
        };
    }
}
