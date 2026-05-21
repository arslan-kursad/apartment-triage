using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure.Channels;

/// <summary>
/// WhatsApp Cloud API channel adapter.
/// Unlike Telegram, WhatsApp is push-based: Meta calls our webhook endpoint.
/// The webhook handler enqueues messages here; ReadMessagesAsync drains the queue.
/// </summary>
public sealed class WhatsAppAdapter : IMessageChannel
{
    private readonly Channel<IncomingMessage> _queue;
    private readonly ILogger<WhatsAppAdapter> _logger;

    public WhatsAppAdapter(ILogger<WhatsAppAdapter> logger)
    {
        _logger = logger;
        // Bounded to prevent unbounded memory growth under load.
        _queue = Channel.CreateBounded<IncomingMessage>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        });
    }

    public ChannelType ChannelType => ChannelType.WhatsApp;

    /// <summary>
    /// Called by the webhook endpoint for each verified incoming message.
    /// Returns false if the queue is full and the message was dropped.
    /// </summary>
    public bool TryEnqueue(IncomingMessage message)
    {
        if (_queue.Writer.TryWrite(message))
            return true;

        _logger.LogWarning("WhatsApp queue full — dropped message {ExternalId}", message.ExternalId);
        return false;
    }

    public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync(cancellationToken))
            yield return message;
    }

    public async Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        // TODO Day 10: implement via WhatsApp Cloud API POST /messages
        // Requires: WhatsApp:PhoneNumberId + WhatsApp:AccessToken from configuration
        _logger.LogWarning(
            "WhatsApp SendAsync not yet implemented — would send to {RecipientId}: {Text}",
            recipientId, text);
        await Task.CompletedTask;
    }

    /// <summary>Verifies X-Hub-Signature-256 header against HMAC-SHA256(appSecret, body).</summary>
    public static bool VerifySignature(string appSecret, byte[] body, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith("sha256="))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(body);
        var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signatureHeader));
    }

    /// <summary>Parses a WhatsApp Cloud API webhook payload into IncomingMessages.</summary>
    public static IEnumerable<IncomingMessage> ParseWebhookPayload(JsonElement root)
    {
        // WhatsApp payload structure:
        // { "entry": [{ "changes": [{ "value": { "messages": [{ ... }] } }] }] }
        if (!root.TryGetProperty("entry", out var entries))
            yield break;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes))
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value))
                    continue;

                if (!value.TryGetProperty("messages", out var messages))
                    continue;

                foreach (var msg in messages.EnumerateArray())
                {
                    // Only process text messages for now
                    if (msg.TryGetProperty("type", out var typeEl) &&
                        typeEl.GetString() != "text")
                        continue;

                    if (!msg.TryGetProperty("id", out var idEl) ||
                        !msg.TryGetProperty("from", out var fromEl) ||
                        !msg.TryGetProperty("text", out var textEl) ||
                        !textEl.TryGetProperty("body", out var bodyEl))
                        continue;

                    // Meta Cloud API v17+ sends timestamp as a string, not a number.
                    var timestampSeconds = msg.TryGetProperty("timestamp", out var tsEl)
                        ? long.Parse(tsEl.GetString()!)
                        : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    yield return new IncomingMessage(
                        ExternalId: idEl.GetString()!,
                        SenderId: fromEl.GetString()!,
                        Text: bodyEl.GetString()!,
                        ReceivedAt: DateTimeOffset.FromUnixTimeSeconds(timestampSeconds));
                }
            }
        }
    }
}
