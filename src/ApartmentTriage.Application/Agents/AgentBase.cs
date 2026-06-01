using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Application.Agents;

public abstract class AgentBase<TIn, TOut> : IAgent<TIn, TOut>
    where TIn : class
    where TOut : class
{
    protected readonly ILogger Logger;
    protected virtual int MaxRetries => 3;

    protected AgentBase(ILogger logger) => Logger = logger;

    public abstract string AgentId { get; }

    protected abstract Task<AgentResult<TOut>> ExecuteCoreAsync(
        TIn input, AgentContext context, CancellationToken cancellationToken);

    // Override to signal escalation to the orchestrator.
    // Called before the non-transient guard so escalation can trigger on any error kind.
    protected virtual bool ShouldEscalate(AgentError error, int attempt) => false;

    // Override in tests to eliminate real delays.
    protected virtual TimeSpan GetRetryDelay(int attempt)
        => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    public async Task<AgentResult<TOut>> ExecuteAsync(
        TIn input, AgentContext context, CancellationToken cancellationToken = default)
    {
        using var scope = Logger.BeginScope(new Dictionary<string, object>
        {
            ["AgentId"] = AgentId,
            ["CorrelationId"] = context.CorrelationId,
            ["MessageId"] = context.MessageId
        });

        // Attribute any Anthropic calls made inside ExecuteCoreAsync to this agent (FinOps).
        var previousAgentId = AgentExecutionContext.CurrentAgentId;
        AgentExecutionContext.CurrentAgentId = AgentId;
        try
        {
        AgentResult<TOut>? lastResult = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            lastResult = await ExecuteCoreAsync(input, context, cancellationToken);

            if (lastResult.IsSuccess)
            {
                Logger.LogInformation("Agent {AgentId} succeeded on attempt {Attempt}", AgentId, attempt);
                return lastResult;
            }

            var error = lastResult.Error!;

            if (ShouldEscalate(error, attempt))
            {
                Logger.LogWarning("Agent {AgentId} signalling escalation on attempt {Attempt}: {Message}",
                    AgentId, attempt, error.Message);
                return AgentResult<TOut>.Fail(error with { Kind = AgentErrorKind.Escalation });
            }

            if (error.Kind != AgentErrorKind.Transient)
            {
                Logger.LogError("Agent {AgentId} non-retryable {Kind} error: {Message}",
                    AgentId, error.Kind, error.Message);
                return lastResult;
            }

            if (attempt < MaxRetries)
            {
                var delay = GetRetryDelay(attempt);
                Logger.LogWarning("Agent {AgentId} transient error on attempt {Attempt}, retrying in {DelayMs}ms",
                    AgentId, attempt, (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        Logger.LogError("Agent {AgentId} exhausted {MaxRetries} retries", AgentId, MaxRetries);
        return lastResult!;
        }
        finally
        {
            AgentExecutionContext.CurrentAgentId = previousAgentId;
        }
    }
}
