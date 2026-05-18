namespace ApartmentTriage.Application.Agents;

public enum AgentErrorKind
{
    Transient,   // Network timeout, 429, 5xx — retry eligible
    Semantic,    // Bad JSON, missing required field — no retry
    Escalation,  // Agent signals need for a stronger model
    Cancelled    // CancellationToken triggered before completion
}
