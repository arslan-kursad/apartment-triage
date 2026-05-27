namespace ApartmentTriage.Application.Channels;

/// <summary>
/// Channel-agnostic representation of an inbound message.
/// Produced by IMessageChannel implementations before any domain processing.
/// </summary>
public sealed record IncomingMessage(
    /// <summary>Channel-native message ID (wamid, Telegram message_id, MOCK-*).</summary>
    string ExternalId,

    /// <summary>Channel-native sender identifier (E.164 phone, Telegram user_id.ToString()).</summary>
    string SenderId,

    string Text,

    DateTimeOffset ReceivedAt,

    /// <summary>BCP-47 language code from the channel (e.g. "tr", "en"). Null when not available.</summary>
    string? LanguageCode = null,

    /// <summary>Raw image bytes downloaded from the channel CDN. Null when message has no photo.</summary>
    byte[]? ImageData = null,

    /// <summary>MIME type of the image, e.g. "image/jpeg". Null when ImageData is null.</summary>
    string? ImageMimeType = null
);
