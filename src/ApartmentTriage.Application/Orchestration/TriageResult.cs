using ApartmentTriage.Application.Agents;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Orchestration;

public sealed class TriageResult
{
    public bool IsSuccess { get; }
    public IReadOnlyList<Ticket> Tickets { get; }
    public AgentError? Error { get; }
    public bool EscalatedToSonnet { get; }

    /// <summary>
    /// Ambiguity reasons from the final classifier output. Non-empty signals that the
    /// caller should send a clarification message via IMessageChannel (Option C pattern).
    /// </summary>
    public IReadOnlyList<AmbiguityReason> AmbiguityReasons { get; }

    /// <summary>
    /// Ready-to-send reply text built by TriageOrchestrator. Null when no reply should be sent
    /// (e.g. pipeline error handled upstream, or Announcement category).
    /// </summary>
    public string? ReplyText { get; }

    private TriageResult(
        IReadOnlyList<Ticket> tickets,
        bool escalated,
        IReadOnlyList<AmbiguityReason> ambiguityReasons,
        string? replyText)
    {
        IsSuccess = true;
        Tickets = tickets;
        EscalatedToSonnet = escalated;
        AmbiguityReasons = ambiguityReasons;
        ReplyText = replyText;
    }

    private TriageResult(AgentError error)
    {
        IsSuccess = false;
        Tickets = [];
        Error = error;
        AmbiguityReasons = [];
    }

    public static TriageResult Ok(
        IReadOnlyList<Ticket> tickets,
        bool escalated = false,
        IReadOnlyList<AmbiguityReason>? ambiguityReasons = null,
        string? replyText = null)
        => new(tickets, escalated, ambiguityReasons ?? [], replyText);

    public static TriageResult Fail(AgentError error) => new(error);
}
