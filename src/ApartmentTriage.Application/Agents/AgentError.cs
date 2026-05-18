namespace ApartmentTriage.Application.Agents;

public sealed record AgentError(
    AgentErrorKind Kind,
    string Message,
    Exception? Exception = null);
