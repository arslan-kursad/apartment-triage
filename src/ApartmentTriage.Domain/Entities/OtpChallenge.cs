using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Domain.Entities;

/// <summary>
/// A one-time login code challenge. Issued when an authorized resident initiates login
/// from their channel (Telegram /login), verified on the web by entering the code.
/// The plaintext code is never stored — only its SHA-256 hash. Single-use, short-lived.
/// </summary>
public sealed class OtpChallenge
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Channel-native identity the code was issued to and sent to.
    /// Telegram → TelegramId (chatId) as string; WhatsApp → E.164 number.
    /// </summary>
    public string Identifier { get; private set; } = null!;

    public ChannelType Channel { get; private set; }

    /// <summary>SHA-256 hex of the 6-digit code. Plaintext is never persisted.</summary>
    public string CodeHash { get; private set; } = null!;

    public DateTime ExpiresAt { get; private set; }

    /// <summary>Verification attempts recorded against this challenge.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Set when the code is successfully redeemed. Null = unused.</summary>
    public DateTime? ConsumedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private OtpChallenge() { } // EF Core

    public static OtpChallenge Create(
        string identifier,
        ChannelType channel,
        string codeHash,
        DateTime expiresAt)
    {
        return new OtpChallenge
        {
            Id = Guid.NewGuid(),
            Identifier = identifier,
            Channel = channel,
            CodeHash = codeHash,
            ExpiresAt = expiresAt,
            AttemptCount = 0,
            ConsumedAt = null,
            CreatedAt = DateTime.UtcNow
        };
    }

    public bool IsConsumed => ConsumedAt.HasValue;

    public bool IsExpired(DateTime now) => now >= ExpiresAt;

    /// <summary>Redeemable: not consumed, not expired, and under the attempt ceiling.</summary>
    public bool IsRedeemable(DateTime now, int maxAttempts)
        => !IsConsumed && !IsExpired(now) && AttemptCount < maxAttempts;

    public void RegisterAttempt() => AttemptCount++;

    public void Consume() => ConsumedAt = DateTime.UtcNow;
}
