using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Domain.Entities;

/// <summary>
/// Maintenance / complaint ticket — one per distinct issue (multi-ticket strategy per taxonomy.v3.yaml).
/// Secondary issues stored as JSON in <see cref="SecondaryIssuesJson"/> until Day 8+ enricher needs them.
/// </summary>
public sealed class Ticket
{
    public Guid Id { get; private set; }
    public Guid ResidentId { get; private set; }
    public Guid SourceMessageId { get; private set; }

    public TicketCategory Category { get; private set; }
    public TicketSeverity Severity { get; private set; }
    public TicketStatus Status { get; private set; }

    /// <summary>Free-text location extracted by Classifier. Max 100 chars. Nullable = not specified.</summary>
    public string? LocationHint { get; private set; }

    /// <summary>
    /// Cause-effect context from orchestrator rule (effect snippets, swap rationale).
    /// Stored as free-text/JSON; not operational notes.
    /// </summary>
    public string? Context { get; private set; }

    /// <summary>Operational notes (technician, manager, follow-up).</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Secondary issues JSON blob — raw Classifier output stored for enricher / Day 8.
    /// Schema: [{category, severity, snippet, location_hint, causal_relation}].
    /// </summary>
    public string? SecondaryIssuesJson { get; private set; }

    /// <summary>384-dim multilingual-e5-small embedding. Null until EnricherAgent processes the ticket.</summary>
    public float[]? EmbeddingVector { get; private set; }

    // Router decision — null until RouterAgent processes the ticket
    public RoutingAction? RoutingAction { get; private set; }

    /// <summary>
    /// JSON-serialized AmbiguityReason[] from ClassifierOutput. Null when no ambiguity.
    /// Schema: ["missing_location","category_ambiguous",...].
    /// </summary>
    public string? AmbiguityReasonsJson { get; private set; }

    // Emergency layer
    public bool IsEmergency { get; private set; }
    public bool EmergencyConfirmedByLlm { get; private set; }

    // Classifier confidence (dual — see taxonomy.v3.yaml § confidence)
    public ConfidenceLevel CategoryConfidence { get; private set; }
    public ConfidenceLevel EmergencyConfidence { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // Navigation
    public Resident? Resident { get; private set; }
    public Message? SourceMessage { get; private set; }

    private Ticket() { } // EF Core

    public static Ticket Create(
        Guid residentId,
        Guid sourceMessageId,
        TicketCategory category,
        TicketSeverity severity,
        ConfidenceLevel categoryConfidence,
        ConfidenceLevel emergencyConfidence,
        string? locationHint = null,
        bool isEmergency = false,
        bool emergencyConfirmedByLlm = false,
        string? secondaryIssuesJson = null)
    {
        var now = DateTime.UtcNow;
        return new Ticket
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            SourceMessageId = sourceMessageId,
            Category = category,
            Severity = severity,
            Status = TicketStatus.Open,
            LocationHint = locationHint is { Length: > 100 } ? locationHint[..100] : locationHint,
            IsEmergency = isEmergency,
            EmergencyConfirmedByLlm = emergencyConfirmedByLlm,
            CategoryConfidence = categoryConfidence,
            EmergencyConfidence = emergencyConfidence,
            SecondaryIssuesJson = secondaryIssuesJson,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateStatus(TicketStatus status)
    {
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetContext(string context)
    {
        Context = context;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNotes(string notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEmbeddingVector(float[] vector)
    {
        EmbeddingVector = vector;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Stores the routing decision produced by RouterAgent.
    /// Called after ticket is persisted (TicketId is known to Enricher).
    /// </summary>
    public void SetRoutingDecision(RoutingAction action, string? ambiguityReasonsJson = null)
    {
        RoutingAction = action;
        AmbiguityReasonsJson = ambiguityReasonsJson;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>KVKK PII redaction — irreversible. Call only from AnonymizationService.</summary>
    public void RedactPii()
    {
        LocationHint = null;
        SecondaryIssuesJson = null;
        Notes = null;
        Context = null;
        EmbeddingVector = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Upgrades severity by one level (capped at Urgent).
    /// Used by orchestrator when effect_of_primary severity >= Medium — taxonomy rule.
    /// </summary>
    public void UpgradeSeverity()
    {
        Severity = Severity switch
        {
            TicketSeverity.Low    => TicketSeverity.Medium,
            TicketSeverity.Medium => TicketSeverity.High,
            TicketSeverity.High   => TicketSeverity.Urgent,
            _                     => Severity // already Urgent
        };
        UpdatedAt = DateTime.UtcNow;
    }
}
