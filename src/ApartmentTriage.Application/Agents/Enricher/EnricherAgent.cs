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

        var confidence = ComputeConfidence(similar, input.ClassifiedCategory);

        Logger.LogInformation(
            "EnricherAgent: ticket {TicketId} — {SimilarCount} similar tickets, confidence {Confidence}",
            input.TicketId, similar.Count, confidence);

        return AgentResult<EnricherOutput>.Ok(new EnricherOutput(
            EmbeddingVector: vector,
            SimilarTickets: similar,
            EnrichedContext: null,  // search-only mode; LLM enrichment deferred to Day 9+
            ConfidenceLevel: confidence));
    }

    // Category consensus — not an absolute cosine threshold — is the trustworthy signal.
    // Turkish maintenance complaints share enough surface vocabulary that even an unrelated
    // message scores a moderately-high cosine against some ticket: an off-topic noise
    // complaint still hit ~0.89 against a plumbing ticket, barely below a genuinely-related
    // plumbing match at ~0.92 (measured on the real pgvector path — ADR-0016). Any fixed
    // cosine cut that admits the 0.92 match also admits the 0.89 false one, so a threshold
    // can't separate them without overfitting to a handful of probes. What does separate a
    // useful enrichment from a spurious one is whether the nearest past tickets corroborate
    // the category the Classifier already assigned.
    private static ConfidenceLevel ComputeConfidence(
        IReadOnlyList<SimilarTicket> similar, TicketCategory classifiedCategory)
    {
        if (similar.Count == 0)
            return ConfidenceLevel.Low;

        // Nearest neighbour agrees with the classification → strongest corroboration.
        if (similar[0].Category == classifiedCategory)
            return ConfidenceLevel.High;

        // Classified category present further down the top-K → partial corroboration.
        if (similar.Any(s => s.Category == classifiedCategory))
            return ConfidenceLevel.Medium;

        // No neighbour shares the classified category → nothing trustworthy to enrich with,
        // regardless of raw cosine (this is the unrelated-complaint case).
        return ConfidenceLevel.Low;
    }
}
