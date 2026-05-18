namespace ApartmentTriage.Application.Agents;

public sealed record AgentContext(
    Guid CorrelationId,
    Guid MessageId,
    Guid ResidentId,
    DateTimeOffset ReceivedAt);
