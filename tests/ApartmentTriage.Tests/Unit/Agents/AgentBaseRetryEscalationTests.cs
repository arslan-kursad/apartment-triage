using ApartmentTriage.Application.Agents;
using ApartmentTriage.Application.Agents.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Agents;

// ── Test doubles ────────────────────────────────────────────────────────────

/// <summary>Configurable AgentBase double — no real HTTP, no real delays.</summary>
internal sealed class FakeAgent : AgentBase<string, string>
{
    private readonly Queue<AgentResult<string>> _results;
    private readonly Func<AgentError, int, bool> _shouldEscalate;
    private int _attemptCount;

    public FakeAgent(
        Queue<AgentResult<string>> results,
        Func<AgentError, int, bool>? shouldEscalate = null)
        : base(NullLogger.Instance)
    {
        _results = results;
        _shouldEscalate = shouldEscalate ?? ((_, _) => false);
    }

    public override string AgentId => "fake";
    public int AttemptCount => _attemptCount;

    protected override Task<AgentResult<string>> ExecuteCoreAsync(
        string input, AgentContext context, CancellationToken cancellationToken)
    {
        _attemptCount++;
        return Task.FromResult(_results.Dequeue());
    }

    protected override bool ShouldEscalate(AgentError error, int attempt)
        => _shouldEscalate(error, attempt);

    // Zero delay so unit tests run instantly.
    protected override TimeSpan GetRetryDelay(int attempt) => TimeSpan.Zero;
}

/// <summary>
/// IAnthropicClient fake — queued responses or exceptions, no HTTP involved.
/// Demonstrates the IAnthropicClient contract without hitting the real API.
/// </summary>
internal sealed class FakeAnthropicClient : IAnthropicClient
{
    private readonly Queue<Func<AnthropicResponse>> _behaviors;

    public FakeAnthropicClient(params Func<AnthropicResponse>[] behaviors)
        => _behaviors = new Queue<Func<AnthropicResponse>>(behaviors);

    public Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(_behaviors.Dequeue()());
}

/// <summary>
/// Minimal agent that wraps IAnthropicClient, mapping HttpRequestException → Transient
/// and other exceptions → Semantic. Used to verify IAnthropicClient is mockable.
/// </summary>
internal sealed class FakeAnthropicAgent : AgentBase<string, string>
{
    private readonly IAnthropicClient _client;

    public FakeAnthropicAgent(IAnthropicClient client) : base(NullLogger.Instance)
        => _client = client;

    public override string AgentId => "fake-anthropic";

    protected override async Task<AgentResult<string>> ExecuteCoreAsync(
        string input, AgentContext context, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.CompleteAsync(
                new AnthropicRequest(AnthropicModels.Haiku45, "system", [new AnthropicMessage("user", input)]),
                cancellationToken);

            return AgentResult<string>.Ok(response.Content);
        }
        catch (HttpRequestException ex)
        {
            return AgentResult<string>.Fail(new AgentError(AgentErrorKind.Transient, ex.Message, ex));
        }
        catch (Exception ex)
        {
            return AgentResult<string>.Fail(new AgentError(AgentErrorKind.Semantic, ex.Message, ex));
        }
    }

    protected override TimeSpan GetRetryDelay(int attempt) => TimeSpan.Zero;
}

// ── Tests ────────────────────────────────────────────────────────────────────

public class AgentBaseRetryEscalationTests
{
    private static AgentContext TestContext() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Retries_TransientError_SucceedsOnThirdAttempt()
    {
        var results = new Queue<AgentResult<string>>([
            AgentResult<string>.Fail(new AgentError(AgentErrorKind.Transient, "timeout")),
            AgentResult<string>.Fail(new AgentError(AgentErrorKind.Transient, "timeout")),
            AgentResult<string>.Ok("success on attempt 3")
        ]);

        var agent = new FakeAgent(results);
        var result = await agent.ExecuteAsync("input", TestContext());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("success on attempt 3");
        agent.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task DoesNotRetry_SemanticError_ReturnsImmediately()
    {
        // Semantic (parse) errors are not retryable — a second call with the same
        // prompt will produce the same broken output.
        var results = new Queue<AgentResult<string>>([
            AgentResult<string>.Fail(new AgentError(AgentErrorKind.Semantic, "JSON parse failed"))
        ]);

        var agent = new FakeAgent(results);
        var result = await agent.ExecuteAsync("input", TestContext());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(AgentErrorKind.Semantic);
        agent.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Escalates_WhenShouldEscalateReturnsTrue()
    {
        // ShouldEscalate fires before the non-transient guard, so escalation
        // can be signalled on any error kind including Semantic.
        var results = new Queue<AgentResult<string>>([
            AgentResult<string>.Fail(new AgentError(AgentErrorKind.Semantic, "low confidence — need stronger model"))
        ]);

        var agent = new FakeAgent(results, shouldEscalate: (_, _) => true);
        var result = await agent.ExecuteAsync("input", TestContext());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(AgentErrorKind.Escalation);
        agent.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ExhaustsMaxRetries_ReturnsLastTransientFailure()
    {
        var results = new Queue<AgentResult<string>>([
            AgentResult<string>.Fail(new AgentError(AgentErrorKind.Transient, "timeout")),
            AgentResult<string>.Fail(new AgentError(AgentErrorKind.Transient, "timeout")),
            AgentResult<string>.Fail(new AgentError(AgentErrorKind.Transient, "timeout"))
        ]);

        var agent = new FakeAgent(results);
        var result = await agent.ExecuteAsync("input", TestContext());

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(AgentErrorKind.Transient);
        agent.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task AnthropicClient_IsMockable_AgentReceivesContent()
    {
        var fakeClient = new FakeAnthropicClient(
            () => new AnthropicResponse("kategori: PLUMBING", "end_turn",
                new AnthropicUsage(100, 50, 0, 100)));

        var agent = new FakeAnthropicAgent(fakeClient);
        var result = await agent.ExecuteAsync("su akıyor", TestContext());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("kategori: PLUMBING");
    }
}
