using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Channels;

/// <summary>
/// Abstraction over all inbound/outbound message transports (WhatsApp, Telegram, Mock).
/// Implementations live in Infrastructure; adapters are swappable via DI.
/// </summary>
public interface IMessageChannel
{
    ChannelType ChannelType { get; }

    /// <summary>
    /// Streams incoming messages until cancellation.
    /// Each item is delivered exactly once per call; backpressure is channel-specific.
    /// </summary>
    IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a text reply to the given channel-native recipient identifier.</summary>
    Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default);
}
