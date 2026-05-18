using ApartmentTriage.Application.Agents;
using ApartmentTriage.Domain.Entities;

namespace ApartmentTriage.Application.Orchestration;

public sealed class TriageResult
{
    public bool IsSuccess { get; }
    public IReadOnlyList<Ticket> Tickets { get; }
    public AgentError? Error { get; }
    public bool EscalatedToSonnet { get; }

    private TriageResult(IReadOnlyList<Ticket> tickets, bool escalated)
    {
        IsSuccess = true;
        Tickets = tickets;
        EscalatedToSonnet = escalated;
    }

    private TriageResult(AgentError error)
    {
        IsSuccess = false;
        Tickets = [];
        Error = error;
    }

    public static TriageResult Ok(IReadOnlyList<Ticket> tickets, bool escalated = false)
        => new(tickets, escalated);

    public static TriageResult Fail(AgentError error) => new(error);
}
