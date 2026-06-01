using System.Globalization;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Domain;

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

    /// <summary>Contact phone for reaching the resident — NOT a channel identity. May differ from WhatsAppNumber.</summary>
    public string? ContactPhone { get; private set; }

    /// <summary>"tr" or "en". Detected from channel language_code or message content on first contact.</summary>
    public string PreferredLanguage { get; private set; } = "tr";

    /// <summary>Soft-delete flag. False means resident is deactivated but data retained (KVKK).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Authorization role for dashboard access. Additive — default <see cref="ResidentRole.None"/>
    /// (a plain resident cannot log in). Set to <see cref="ResidentRole.Manager"/> via bootstrap
    /// or admin action. Not a channel identity.
    /// </summary>
    public ResidentRole Role { get; private set; } = ResidentRole.None;

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

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Normalize display name to Turkish uppercase ("i"→"İ", "ı" preserved). Returns null for empty input.</summary>
    private static string? NormalizeDisplayName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpper(TurkishCulture);
    }

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
            DisplayName = NormalizeDisplayName(displayName),
            WhatsAppNumber = PhoneNumberNormalizer.Normalize(whatsAppNumber),
            TelegramId = telegramId,
            PreferredLanguage = preferredLanguage is "tr" or "en" ? preferredLanguage : "tr",
            IsActive = true,
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
        string? apartmentNumber = null,
        string? whatsAppNumber = null,
        long? telegramId = null,
        string? contactPhone = null)
    {
        if (displayName is not null) DisplayName = NormalizeDisplayName(displayName);
        if (apartmentNumber is not null) ApartmentNumber = apartmentNumber.Trim();
        if (whatsAppNumber is not null) WhatsAppNumber = PhoneNumberNormalizer.Normalize(whatsAppNumber);
        if (telegramId.HasValue) TelegramId = telegramId;
        if (contactPhone is not null) ContactPhone = PhoneNumberNormalizer.Normalize(contactPhone);
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>Assigns the authorization role. Used by bootstrap and admin role management.</summary>
    public void SetRole(ResidentRole role) => Role = role;

    /// <summary>Clears Telegram channel identity (e.g. when merging a ghost row into the WhatsApp manager record).</summary>
    public void ClearTelegramId() => TelegramId = null;

    /// <summary>KVKK anonymization — irreversible. Call only from AnonymizationService.</summary>
    public void Anonymize()
    {
        DisplayName = null;
        WhatsAppNumber = "[redacted]";
        TelegramId = null;
        ApartmentNumber = "***";
    }
}
