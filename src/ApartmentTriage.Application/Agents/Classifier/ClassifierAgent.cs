using System.Text.Json;
using System.Text.Json.Serialization;
using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Application.Agents.Classifier;

public sealed class ClassifierAgent : AgentBase<ClassifierInput, ClassifierOutput>
{
    private readonly IAnthropicClient _client;
    private readonly string _model;

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
        }
    };

    public ClassifierAgent(IAnthropicClient client, string model, ILogger<ClassifierAgent> logger)
        : base(logger)
    {
        _client = client;
        _model = model;
    }

    public override string AgentId => $"classifier/{_model}";

    // Form A (error-based) escalation not applicable to Classifier.
    // Form B (quality-based, low confidence) is handled by TriageOrchestrator.
    protected override bool ShouldEscalate(AgentError error, int attempt) => false;

    protected override async Task<AgentResult<ClassifierOutput>> ExecuteCoreAsync(
        ClassifierInput input, AgentContext context, CancellationToken cancellationToken)
    {
        var userMessage = BuildUserMessage(input);

        var request = new AnthropicRequest(
            Model: _model,
            SystemPrompt: SystemPrompt,
            Messages: [new AnthropicMessage("user", userMessage, input.ImageData, input.ImageMimeType)],
            MaxTokens: 512,
            CacheSystemPrompt: true);

        AnthropicResponse response;
        try
        {
            response = await _client.CompleteAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (IsRetryable(ex))
        {
            return AgentResult<ClassifierOutput>.Fail(new AgentError(
                AgentErrorKind.Transient, $"Anthropic HTTP error: {ex.Message}", ex));
        }
        catch (HttpRequestException ex)
        {
            return AgentResult<ClassifierOutput>.Fail(new AgentError(
                AgentErrorKind.Semantic, $"Anthropic HTTP error (non-retryable): {ex.Message}", ex));
        }
        catch (TaskCanceledException ex)
        {
            return AgentResult<ClassifierOutput>.Fail(new AgentError(
                AgentErrorKind.Cancelled, "Request cancelled", ex));
        }

        return ParseResponse(response.Content);
    }

    private AgentResult<ClassifierOutput> ParseResponse(string raw)
    {
        string json;
        try
        {
            json = ExtractJson(raw);
        }
        catch (Exception ex)
        {
            var safeRaw = raw.Length > 200 ? raw[..200] + "…" : raw;
            Logger.LogWarning("Classifier: could not extract JSON from response. Raw: {Raw}", safeRaw);
            return AgentResult<ClassifierOutput>.Fail(new AgentError(
                AgentErrorKind.Semantic, "No JSON object found in LLM response", ex));
        }

        LlmOutput? dto;
        try
        {
            dto = JsonSerializer.Deserialize<LlmOutput>(json, ParseOptions);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning("Classifier: JSON parse error. Json: {Json}", json);
            return AgentResult<ClassifierOutput>.Fail(new AgentError(
                AgentErrorKind.Semantic, $"JSON deserialization failed: {ex.Message}", ex));
        }

        if (dto is null)
            return AgentResult<ClassifierOutput>.Fail(new AgentError(
                AgentErrorKind.Semantic, "Deserialized LLM output was null"));

        if (!dto.IsValid(out var validationError))
            return AgentResult<ClassifierOutput>.Fail(new AgentError(
                AgentErrorKind.Semantic, validationError));

        var output = new ClassifierOutput(
            Category: dto.Category!.Value,
            Severity: dto.Severity!.Value,
            CategoryConfidence: dto.CategoryConfidence!.Value,
            IsEmergency: dto.IsEmergency,
            EmergencyConfidence: dto.EmergencyConfidence!.Value,
            LocationHint: dto.LocationHint is { Length: > 100 } ? dto.LocationHint[..100] : dto.LocationHint,
            SecondaryIssues: dto.SecondaryIssues?
                .Select(s => new ClassifierSecondaryIssue(
                    s.Category!.Value,
                    s.Severity!.Value,
                    s.Snippet ?? string.Empty,
                    s.LocationHint is { Length: > 100 } ? s.LocationHint[..100] : s.LocationHint,
                    s.CausalRelation!.Value))
                .ToList()
                ?? [],
            AmbiguityReasons: dto.AmbiguityReasons ?? [],
            Rationale: dto.Rationale);

        return AgentResult<ClassifierOutput>.Ok(output);
    }

    private static string BuildUserMessage(ClassifierInput input)
    {
        var phrases = input.MatchedPhrases.Count > 0
            ? string.Join(", ", input.MatchedPhrases.Select(p => $"\"{p}\""))
            : "none";

        var hasImage = input.ImageData is not null;

        return $"""
            Channel: {input.ChannelType}
            Has image: {(hasImage ? "YES" : "NO")}
            Emergency suspected (phrase match): {(input.EmergencySuspected ? "YES" : "NO")}
            Matched phrases: {phrases}

            Message:
            {input.RawText}
            """;
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < 0 || end <= start)
            throw new InvalidOperationException("No JSON object found");
        return text[start..(end + 1)];
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        var status = (int?)ex.StatusCode;
        return status is null or 429 or >= 500;
    }

    // ─── LLM output DTOs (private — only used for deserialization) ────────────

    private sealed class LlmOutput
    {
        public TicketCategory? Category { get; set; }
        public TicketSeverity? Severity { get; set; }
        public ConfidenceLevel? CategoryConfidence { get; set; }
        public bool IsEmergency { get; set; }
        public ConfidenceLevel? EmergencyConfidence { get; set; }
        public string? LocationHint { get; set; }
        public List<LlmSecondaryIssue>? SecondaryIssues { get; set; }
        public List<AmbiguityReason>? AmbiguityReasons { get; set; }
        public string? Rationale { get; set; }

        public bool IsValid(out string error)
        {
            if (Category is null) { error = "Missing required field: category"; return false; }
            if (Severity is null) { error = "Missing required field: severity"; return false; }
            if (CategoryConfidence is null) { error = "Missing required field: category_confidence"; return false; }
            if (EmergencyConfidence is null) { error = "Missing required field: emergency_confidence"; return false; }
            error = string.Empty;
            return true;
        }
    }

    private sealed class LlmSecondaryIssue
    {
        public TicketCategory? Category { get; set; }
        public TicketSeverity? Severity { get; set; }
        public string? Snippet { get; set; }
        public string? LocationHint { get; set; }
        public CausalRelation? CausalRelation { get; set; }
    }

    // ─── System prompt ────────────────────────────────────────────────────────

    private const string SystemPrompt = """
        You are a building maintenance triage classifier for a Turkish residential apartment building.
        Analyze incoming WhatsApp messages and classify them. Respond with a single JSON object only — no prose, no markdown.

        CATEGORIES (use exact snake_case value):
        plumbing         — indoor water supply, drainage, leaks, clogs (internal plumbing only)
        electrical       — wiring, outlets, breakers, lighting circuits, electrical panels, exposed cables
        gas              — natural gas, combi boiler gas side, gas odor/leak
        heating_cooling  — boiler heat side, radiators, AC, climate equipment
        elevator         — elevator malfunction, stuck, door issues
        structural       — cracks, roof, windows, doors, balcony, facade, external water ingress
        common_area      — stairwell, entrance, parking, garden, cleaning
        pest             — rodents, insects, pigeons, infestation
        noise            — noise complaint
        neighbor_dispute — non-noise inter-resident dispute
        billing          — dues, invoices, payment disputes
        security         — locks, cameras, suspicious activity, break-ins
        announcement     — informational message (utility outage notice, building announcement) — NOT a ticket
        other            — none of the above (use sparingly, target <5% of traffic)

        ELECTRICAL CLASSIFICATION — Turkish signals that always map to category=electrical:
        açık kablo / kablolar dışarıda / açık tel / bare wire → category=electrical, minimum severity=high
        sigorta panosu / elektrik panosu / anahtar kutusu / breaker box → category=electrical
        kıvılcım / spark → category=electrical, severity=urgent, is_emergency=true
        elektrik tesisatı / wiring / devre / circuit → category=electrical
        When a combi boiler (kombi) has an electrical fault (panel, wiring, cables), use category=electrical.

        SEVERITY values: low, medium, high, urgent
        Upgrade signals (push toward higher severity): flood, fire, smoke, stuck, sparks, "şu an", "acil", "hemen", all-caps, 3+ exclamations
        Downgrade signals: "küçük", "soru", "acelesi yok", "ne zaman uygun olursa"

        CONFIDENCE values: low, medium, high
        Provide two independent confidence scores:
        - category_confidence: how certain you are about the primary category
        - emergency_confidence: how certain you are about the is_emergency decision

        EMERGENCY: Set is_emergency=true only when there is immediate threat to life or property.
        The user message may include a "Emergency suspected (phrase match): YES/NO" hint — treat as a soft signal only.
        Layer 2 (your) decision has final authority.

        SECONDARY ISSUES: When a message contains multiple distinct problems, list them.
        causal_relation values: independent, effect_of_primary, cause_of_primary
        - independent: separate root cause, different trade needed
        - effect_of_primary: secondary is a symptom/consequence of primary
        - cause_of_primary: secondary IS the root cause; primary is the symptom

        AMBIGUITY REASONS (include if clarification would change the ticket):
        missing_location, missing_severity, category_ambiguous, language_unclear, needs_visual, non_actionable

        CONSTRAINT — non_actionable:
        non_actionable is only appropriate when the message genuinely cannot be acted upon
        (e.g. "bir şey var" with no identifiable issue type whatsoever).
        NEVER use non_actionable when the message describes:
        - Exposed cables, open wires, bare wire (açık kablo, kablolar dışarıda, açık tel)
        - Electrical panel or breaker box (sigorta panosu, elektrik panosu, anahtar kutusu)
        - Sparks or burning smell from electrical source (kıvılcım)
        These messages are always actionable — omit non_actionable from ambiguity_reasons.

        EXAMPLES — correct classification for electrical scenarios:

        Input: "kombinin kabloları açıkta tehlikeli görünüyor"
        {"category":"electrical","severity":"high","category_confidence":"high","is_emergency":false,"emergency_confidence":"medium","location_hint":null,"secondary_issues":[],"ambiguity_reasons":[],"rationale":"Exposed boiler cables are an actionable electrical hazard; inspection required within 24h."}

        Input: "sigorta kabloları dışarıda 2 haftadır bu şekilde riskli"
        {"category":"electrical","severity":"high","category_confidence":"high","is_emergency":false,"emergency_confidence":"medium","location_hint":null,"secondary_issues":[],"ambiguity_reasons":[],"rationale":"Panel cables exposed for 2 weeks — ongoing safety risk, high severity, technician needed."}

        Input: "elektrik panosunda açık tel var, çocuklar var evde"
        {"category":"electrical","severity":"urgent","category_confidence":"high","is_emergency":true,"emergency_confidence":"high","location_hint":null,"secondary_issues":[],"ambiguity_reasons":[],"rationale":"Exposed panel wires with children present — immediate life threat, emergency response required."}

        Input: "anahtar kutusundan kıvılcım çıkıyor"
        {"category":"electrical","severity":"urgent","category_confidence":"high","is_emergency":true,"emergency_confidence":"high","location_hint":null,"secondary_issues":[],"ambiguity_reasons":[],"rationale":"Sparks from switch box indicate active electrical fault — fire risk, immediate emergency."}

        OUTPUT FORMAT (JSON only):
        {
          "category": "<snake_case>",
          "severity": "<low|medium|high|urgent>",
          "category_confidence": "<low|medium|high>",
          "is_emergency": <true|false>,
          "emergency_confidence": "<low|medium|high>",
          "location_hint": "<string or null>",
          "secondary_issues": [
            {
              "category": "<snake_case>",
              "severity": "<low|medium|high|urgent>",
              "snippet": "<exact quote from message or empty string>",
              "location_hint": "<string or null>",
              "causal_relation": "<independent|effect_of_primary|cause_of_primary>"
            }
          ],
          "ambiguity_reasons": [],
          "rationale": "<max 200 chars, in English>"
        }
        """;
}
