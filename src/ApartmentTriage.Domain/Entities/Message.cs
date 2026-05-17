using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Domain.Entities;

/// <summary>
/// Raw incoming message from any channel — immutable after creation except for ProcessedAt.
/// Emergency layer 1 (phrase match) result is stored here; layer 2 result lives on Ticket.
/// </summary>
public sealed class Message
{
    public Guid Id { get; private set; }
    public Guid ResidentId { get; private set; }
    public ChannelType ChannelType { get; private set; }

    /// <summary>Channel-native message ID (WhatsApp wamid, Telegram message_id.ToString(), MOCK-*).</summary>
    public string ExternalMessageId { get; private set; } = string.Empty;

    public string RawText { get; private set; } = string.Empty;

    /// <summary>Layer 1 soft signal: phrase set matched at least one pattern.</summary>
    public bool EmergencySuspected { get; private set; }

    /// <summary>Phrases that triggered EmergencySuspected (subset of emergency_phrases.v2.json).</summary>
    public string[] MatchedPhrases { get; private set; } = [];

    public DateTime ReceivedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    // Navigation
    public Resident? Resident { get; private set; }

    private Message() { } // EF Core

    public static Message Create(
        Guid residentId,
        ChannelType channelType,
        string externalMessageId,
        string rawText,
        DateTime receivedAt,
        bool emergencySuspected = false,
        string[]? matchedPhrases = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawText);

        return new Message
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            ChannelType = channelType,
            ExternalMessageId = externalMessageId,
            RawText = rawText,
            ReceivedAt = receivedAt,
            EmergencySuspected = emergencySuspected,
            MatchedPhrases = matchedPhrases ?? []
        };
    }

    public void MarkProcessed()
    {
        if (ProcessedAt.HasValue)
            throw new InvalidOperationException($"Message {Id} is already processed.");

        ProcessedAt = DateTime.UtcNow;
    }
}
