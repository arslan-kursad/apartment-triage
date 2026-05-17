namespace ApartmentTriage.Domain.Entities;

/// <summary>
/// Apartment resident — source of incoming messages.
/// Channel identifiers (WhatsApp/Telegram) are nullable; a resident may use only one channel.
/// </summary>
public sealed class Resident
{
    public Guid Id { get; private set; }

    /// <summary>Daire numarası, free-text: "5", "12A", "Bodrum 1".</summary>
    public string ApartmentNumber { get; private set; } = string.Empty;

    /// <summary>Display name from channel profile — may be null until first contact.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>WhatsApp phone in E.164 format: +905xxxxxxxxx</summary>
    public string? WhatsAppNumber { get; private set; }

    /// <summary>Telegram user ID (long).</summary>
    public long? TelegramId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // Navigation
    public IReadOnlyCollection<Message> Messages { get; private set; } = [];
    public IReadOnlyCollection<Ticket> Tickets { get; private set; } = [];

    private Resident() { } // EF Core

    public static Resident Create(
        string apartmentNumber,
        string? displayName = null,
        string? whatsAppNumber = null,
        long? telegramId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apartmentNumber);

        return new Resident
        {
            Id = Guid.NewGuid(),
            ApartmentNumber = apartmentNumber.Trim(),
            DisplayName = displayName?.Trim(),
            WhatsAppNumber = whatsAppNumber?.Trim(),
            TelegramId = telegramId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateContactInfo(
        string? displayName = null,
        string? whatsAppNumber = null,
        long? telegramId = null)
    {
        if (displayName is not null) DisplayName = displayName.Trim();
        if (whatsAppNumber is not null) WhatsAppNumber = whatsAppNumber.Trim();
        if (telegramId.HasValue) TelegramId = telegramId;
    }
}
