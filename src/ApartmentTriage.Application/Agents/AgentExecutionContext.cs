namespace ApartmentTriage.Application.Agents;

/// <summary>
/// Ambient, async-flowing marker for the agent currently executing. <see cref="AgentBase{TIn,TOut}"/>
/// sets it around each run so cross-cutting infrastructure (e.g. API-usage instrumentation) can
/// attribute Anthropic calls to the right agent without threading the id through every signature.
/// </summary>
public static class AgentExecutionContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CurrentAgentId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
