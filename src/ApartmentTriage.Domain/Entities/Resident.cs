namespace ApartmentTriage.Domain.Entities;

/// <summary>
/// Apartment resident — source of incoming messages.
/// Channel identifiers (WhatsApp/Telegram) are nullable; a resident may use only one channel.
/// </summary>
public sealed class Resident
{
    public Guid Id { get; private set; }

    /// <summary>Daire numarası, free-text: "5", "12A", "Bodrum 1". Null on first auto-created contact; updated when resident identifies themselves.</summary>
    public string? ApartmentNumber { get; private set; }

    /// <summary>Display name from channel profile — may be null until first contact.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>WhatsApp phone in E.164 format: +905xxxxxxxxx</summary>
    public string? WhatsAppNumber { get; private set; }

    /// <summary>Telegram user ID (long).</summary>
    public long? TelegramId { get; private set; }

    /// <summary>"tr" or "en". Detected from channel language_code or message content on first contact.</summary>
    public string PreferredLanguage { get; private set; } = "tr";

    /// <summary>
    /// Set when a clarification question is sent to the resident. The next incoming message
    /// is treated as the clarification response and updates this ticket instead of creating a new one.
    /// Cleared after the response is processed.
    /// </summary>
    public Guid? PendingClarificationTicketId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // Navigation
    public IReadOnlyCollection<Message> Messages { get; private set; } = new List<Message>();
    public IReadOnlyCollection<Ticket> Tickets { get; private set; } = new List<Ticket>();

    private Resident() { } // EF Core

    public static Resident Create(
        string? apartmentNumber = null,
        string? displayName = null,
        string? whatsAppNumber = null,
        long? telegramId = null,
        string preferredLanguage = "tr")
    {
        return new Resident
        {
            Id = Guid.NewGuid(),
            ApartmentNumber = apartmentNumber?.Trim(),
            DisplayName = displayName?.Trim(),
            WhatsAppNumber = whatsAppNumber?.Trim(),
            TelegramId = telegramId,
            PreferredLanguage = preferredLanguage is "tr" or "en" ? preferredLanguage : "tr",
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetPreferredLanguage(string language)
    {
        if (language is "tr" or "en")
            PreferredLanguage = language;
    }

    public void SetPendingClarification(Guid? ticketId)
        => PendingClarificationTicketId = ticketId;

    public void UpdateContactInfo(
        string? displayName = null,
        string? whatsAppNumber = null,
        long? telegramId = null)
    {
        if (displayName is not null) DisplayName = displayName.Trim();
        if (whatsAppNumber is not null) WhatsAppNumber = whatsAppNumber.Trim();
        if (telegramId.HasValue) TelegramId = telegramId;
    }

    /// <summary>KVKK anonymization — irreversible. Call only from AnonymizationService.</summary>
    public void Anonymize()
    {
        DisplayName = null;
        WhatsAppNumber = "[redacted]";
        TelegramId = null;
        ApartmentNumber = "***";
    }
}
