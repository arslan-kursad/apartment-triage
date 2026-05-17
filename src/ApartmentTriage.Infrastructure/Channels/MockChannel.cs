using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Infrastructure.Channels;

/// <summary>
/// In-memory channel for unit/integration tests and local dev without a real WhatsApp/Telegram connection.
/// Enqueue messages via <see cref="Enqueue"/> before or during iteration.
/// Sent replies accumulate in <see cref="SentMessages"/> for assertion.
/// </summary>
public sealed class MockChannel : IMessageChannel
{
    private readonly Channel<IncomingMessage> _inbound =
        System.Threading.Channels.Channel.CreateUnbounded<IncomingMessage>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly List<SentMessage> _sent = [];

    public ChannelType ChannelType => ChannelType.Mock;

    // ── Inbound control ─────────────────────────────────────────────────────

    /// <summary>Adds a message to the inbound queue.</summary>
    public void Enqueue(IncomingMessage message) =>
        _inbound.Writer.TryWrite(message);

    /// <summary>Signals that no more messages will be enqueued; ReadMessagesAsync will complete.</summary>
    public void Complete() =>
        _inbound.Writer.Complete();

    // ── IMessageChannel ──────────────────────────────────────────────────────

    public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var msg in _inbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return msg;
    }

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        _sent.Add(new SentMessage(recipientId, text, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    // ── Test helpers ─────────────────────────────────────────────────────────

    public IReadOnlyList<SentMessage> SentMessages => _sent.AsReadOnly();

    /// <summary>Convenience factory: creates a unique MOCK-{guid} external ID.</summary>
    public static IncomingMessage BuildMessage(string senderId, string text, DateTimeOffset? at = null) =>
        new(
            ExternalId: $"MOCK-{Guid.NewGuid():N}",
            SenderId: senderId,
            Text: text,
            ReceivedAt: at ?? DateTimeOffset.UtcNow);
}

public sealed record SentMessage(string RecipientId, string Text, DateTimeOffset SentAt);
