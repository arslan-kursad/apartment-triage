namespace ApartmentTriage.Application.Agents;

public sealed class AgentResult<TOut>
{
    public bool IsSuccess { get; }
    public TOut? Value { get; }
    public AgentError? Error { get; }

    private AgentResult(TOut value) { IsSuccess = true; Value = value; }
    private AgentResult(AgentError error) { IsSuccess = false; Error = error; }

    public static AgentResult<TOut> Ok(TOut value) => new(value);
    public static AgentResult<TOut> Fail(AgentError error) => new(error);

    public TResult Match<TResult>(Func<TOut, TResult> onSuccess, Func<AgentError, TResult> onFailure)
        => IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}
