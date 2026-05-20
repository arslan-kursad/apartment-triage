using System.Text.Json;
using System.Text.Json.Serialization;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Application.Agents.Router;

public sealed class RouterAgent : AgentBase<RouterInput, RouterOutput>
{
    private readonly IAnthropicClient _client;
    private readonly string _model;

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public RouterAgent(IAnthropicClient client, string model, ILogger<RouterAgent> logger)
        : base(logger)
    {
        _client = client;
        _model = model;
    }

    public override string AgentId => $"router/{_model}";

    // Rule-based routing handles all deterministic cases.
    // Semantic errors from LLM fallback are not escalatable — log and default to AssignTechnician.
    protected override bool ShouldEscalate(AgentError error, int attempt) => false;

    protected override async Task<AgentResult<RouterOutput>> ExecuteCoreAsync(
        RouterInput input, AgentContext context, CancellationToken cancellationToken)
    {
        // ── Layer 1: Emergency Fast-Path (LLM bypass) ─────────────────────────
        if (input.IsEmergency &&
            input.EmergencyConfidence is ConfidenceLevel.High or ConfidenceLevel.Medium)
        {
            Logger.LogInformation(
                "RouterAgent [{TicketId}] Layer 1 → TriggerEmergency " +
                "(IsEmergency=true, EmergencyConfidence={Conf})",
                input.TicketId, input.EmergencyConfidence);

            return AgentResult<RouterOutput>.Ok(new RouterOutput(
                Action: RoutingAction.TriggerEmergency,
                AssignedTo: null,
                NotificationNote: null,
                ConfidenceLevel: ConfidenceLevel.High));
        }

        // ── Layer 2: Rule-Based (LLM bypass) ──────────────────────────────────
        var ruleResult = ApplyRules(input);
        if (ruleResult is not null)
        {
            Logger.LogInformation(
                "RouterAgent [{TicketId}] Layer 2 → {Action}",
                input.TicketId, ruleResult.Action);
            return AgentResult<RouterOutput>.Ok(ruleResult);
        }

        // ── Layer 3: LLM Fallback ─────────────────────────────────────────────
        Logger.LogInformation(
            "RouterAgent [{TicketId}] Layer 3 → LLM fallback", input.TicketId);

        return await CallLlmAsync(input, cancellationToken);
    }

    // Returns non-null when a rule fires; null means fall through to LLM.
    private static RouterOutput? ApplyRules(RouterInput input)
    {
        // Rule 1: Urgent severity → escalate
        if (input.Severity == TicketSeverity.Urgent)
            return new RouterOutput(
                RoutingAction.EscalateToManager, null, null, ConfidenceLevel.High);

        // Rule 2: NonActionable → archive
        if (input.AmbiguityReasons.Contains(AmbiguityReason.NonActionable))
            return new RouterOutput(
                RoutingAction.Archive, null, null, ConfidenceLevel.High);

        // Rule 3: Any ambiguity → notify resident for clarification
        if (input.AmbiguityReasons.Count > 0)
        {
            var note = BuildClarificationNote(input.AmbiguityReasons);
            return new RouterOutput(
                RoutingAction.NotifyResident, null, note, ConfidenceLevel.High);
        }

        // Rule 4: High-confidence similar ticket → route clear
        if (input.SimilarTickets.Any(t => t.CosineSimilarity > 0.90f))
            return new RouterOutput(
                RoutingAction.AssignTechnician, null, null, ConfidenceLevel.High);

        return null;
    }

    private async Task<AgentResult<RouterOutput>> CallLlmAsync(
        RouterInput input, CancellationToken cancellationToken)
    {
        var userMessage = BuildUserMessage(input);

        var request = new AnthropicRequest(
            Model: _model,
            SystemPrompt: SystemPrompt,
            Messages: [new AnthropicMessage("user", userMessage)],
            MaxTokens: 256,
            CacheSystemPrompt: true);

        AnthropicResponse response;
        try
        {
            response = await _client.CompleteAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (IsRetryable(ex))
        {
            return AgentResult<RouterOutput>.Fail(new AgentError(
                AgentErrorKind.Transient, $"Anthropic HTTP error: {ex.Message}", ex));
        }
        catch (HttpRequestException ex)
        {
            return AgentResult<RouterOutput>.Fail(new AgentError(
                AgentErrorKind.Semantic, $"Anthropic HTTP error (non-retryable): {ex.Message}", ex));
        }
        catch (TaskCanceledException ex)
        {
            return AgentResult<RouterOutput>.Fail(new AgentError(
                AgentErrorKind.Cancelled, "Request cancelled", ex));
        }

        return ParseLlmResponse(response.Content, input.TicketId);
    }

    private AgentResult<RouterOutput> ParseLlmResponse(string raw, Guid ticketId)
    {
        string json;
        try
        {
            var start = raw.IndexOf('{');
            var end   = raw.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
                throw new InvalidOperationException("No JSON object found");
            json = raw[start..(end + 1)];
        }
        catch (Exception)
        {
            var safeRaw = raw.Length > 200 ? raw[..200] + "…" : raw;
            Logger.LogWarning(
                "RouterAgent [{TicketId}] LLM response JSON extract failed — defaulting to AssignTechnician. Raw: {Raw}",
                ticketId, safeRaw);
            return AgentResult<RouterOutput>.Ok(new RouterOutput(
                RoutingAction.AssignTechnician, null, null, ConfidenceLevel.Low));
        }

        LlmOutput? dto;
        try
        {
            dto = JsonSerializer.Deserialize<LlmOutput>(json, ParseOptions);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(
                "RouterAgent [{TicketId}] LLM JSON parse error — defaulting to AssignTechnician. Json: {Json}",
                ticketId, json);
            return AgentResult<RouterOutput>.Ok(new RouterOutput(
                RoutingAction.AssignTechnician, null, null, ConfidenceLevel.Low));
        }

        // LLM is only permitted to return a subset of actions.
        var action = dto?.Action ?? RoutingAction.AssignTechnician;
        if (action is not (RoutingAction.AssignTechnician
                        or RoutingAction.EscalateToManager
                        or RoutingAction.Defer))
        {
            Logger.LogWarning(
                "RouterAgent [{TicketId}] LLM returned disallowed action {Action} — defaulting to AssignTechnician",
                ticketId, action);
            action = RoutingAction.AssignTechnician;
        }

        return AgentResult<RouterOutput>.Ok(new RouterOutput(
            Action: action,
            AssignedTo: null,
            NotificationNote: null,
            ConfidenceLevel: ConfidenceLevel.Medium));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildClarificationNote(IReadOnlyList<AmbiguityReason> reasons)
    {
        var parts = reasons.Select(r => r switch
        {
            AmbiguityReason.MissingLocation  => "konum belirtilmemiş",
            AmbiguityReason.MissingSeverity  => "sorunun aciliyeti belirsiz",
            AmbiguityReason.CategoryAmbiguous => "sorun türü netleştirilmeli",
            AmbiguityReason.LanguageUnclear  => "mesaj anlaşılamadı, lütfen tekrar yaz",
            AmbiguityReason.NeedsVisual      => "fotoğraf veya video gerekli",
            _                                => null
        }).Where(p => p is not null);

        return string.Join("; ", parts);
    }

    private static string BuildUserMessage(RouterInput input)
    {
        var similar = input.SimilarTickets.Count > 0
            ? $"{input.SimilarTickets.Count} similar ticket(s), top similarity: {input.SimilarTickets[0].CosineSimilarity:F2}"
            : "none";

        return $"""
            Category: {input.Category}
            Severity: {input.Severity}
            Category confidence: {input.CategoryConfidence}
            Similar tickets: {similar}

            Message:
            {input.RawText}
            """;
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        var status = (int?)ex.StatusCode;
        return status is null or 429 or >= 500;
    }

    // ── LLM output DTO ───────────────────────────────────────────────────────

    private sealed class LlmOutput
    {
        public RoutingAction Action { get; set; }
    }

    // ── System prompt ────────────────────────────────────────────────────────

    private const string SystemPrompt = """
        You are a maintenance ticket router for a Turkish residential apartment building.
        A ticket has already been classified and enriched. Your job is to decide the routing action.
        Respond with a single JSON object only — no prose, no markdown.

        ALLOWED ACTIONS (choose exactly one):
        assign_technician   — standard maintenance; schedule a technician
        escalate_to_manager — high impact, time-sensitive, or recurring issue
        defer               — low priority; no immediate action needed

        ESCALATE when:
        - Severity is High and issue affects multiple residents or common areas
        - Issue is recurring (similar tickets in history)
        - Potential safety risk below emergency threshold

        DEFER when:
        - Severity is Low
        - Cosmetic issue (paint, minor scratch, etc.)
        - Resident explicitly said no urgency

        DEFAULT to assign_technician when uncertain.

        OUTPUT FORMAT (JSON only):
        { "action": "<assign_technician|escalate_to_manager|defer>" }
        """;
}
