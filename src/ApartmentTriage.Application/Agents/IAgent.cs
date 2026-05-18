namespace ApartmentTriage.Application.Agents;

public interface IAgent<TIn, TOut>
{
    string AgentId { get; }
    Task<AgentResult<TOut>> ExecuteAsync(TIn input, AgentContext context, CancellationToken cancellationToken = default);
}
