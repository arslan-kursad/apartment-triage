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

    private TriageResult(
        IReadOnlyList<Ticket> tickets,
        bool escalated,
        IReadOnlyList<AmbiguityReason> ambiguityReasons)
    {
        IsSuccess = true;
        Tickets = tickets;
        EscalatedToSonnet = escalated;
        AmbiguityReasons = ambiguityReasons;
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
        IReadOnlyList<AmbiguityReason>? ambiguityReasons = null)
        => new(tickets, escalated, ambiguityReasons ?? []);

    public static TriageResult Fail(AgentError error) => new(error);
}
