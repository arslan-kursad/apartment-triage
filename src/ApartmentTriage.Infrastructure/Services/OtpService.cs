using System.Security.Cryptography;
using System.Text;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Application.Services;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApartmentTriage.Infrastructure.Services;

/// <summary>
/// Issues and verifies one-time login codes. Codes are 6-digit, cryptographically random,
/// stored only as SHA-256 hashes, single-use, and short-lived. See <see cref="IOtpService"/>.
/// </summary>
public sealed class OtpService : IOtpService
{
    private readonly IOtpChallengeRepository _challenges;
    private readonly IResidentRepository _residents;
    private readonly IMessageChannel _telegram;
    private readonly IMessageChannel _whatsApp;
    private readonly ILogger<OtpService> _logger;

    private readonly int _expiryMinutes;
    private readonly int _maxAttempts;
    private readonly int _maxChallengesPerWindow;
    private readonly int _challengeWindowMinutes;

    public OtpService(
        IOtpChallengeRepository challenges,
        IResidentRepository residents,
        [FromKeyedServices(ChannelType.Telegram)] IMessageChannel telegram,
        [FromKeyedServices(ChannelType.WhatsApp)] IMessageChannel whatsApp,
        IConfiguration configuration,
        ILogger<OtpService> logger)
    {
        _challenges = challenges;
        _residents = residents;
        _telegram = telegram;
        _whatsApp = whatsApp;
        _logger = logger;

        _expiryMinutes = configuration.GetValue("Auth:OtpExpiryMinutes", 5);
        _maxAttempts = configuration.GetValue("Auth:MaxAttempts", 5);
        _maxChallengesPerWindow = configuration.GetValue("Auth:MaxChallengesPerWindow", 3);
        _challengeWindowMinutes = configuration.GetValue("Auth:ChallengeWindowMinutes", 15);
    }

    public async Task<OtpSendStatus> GenerateAndSendAsync(
        string identifier,
        ChannelType channel,
        CancellationToken cancellationToken = default)
    {
        var resident = await ResolveResidentAsync(identifier, channel, cancellationToken);

        // Enumeration-safe: unknown and not-allowed are indistinguishable to the caller, and nothing is sent.
        if (resident is null || !IsAuthorizedForLogin(resident))
        {
            _logger.LogDebug(
                "OTP request for unauthorized {Channel} identifier — no code issued", channel);
            return OtpSendStatus.Unauthorized;
        }

        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-_challengeWindowMinutes);
        var recentCount = await _challenges.CountSinceAsync(identifier, channel, windowStart, cancellationToken);
        if (recentCount >= _maxChallengesPerWindow)
        {
            _logger.LogInformation(
                "OTP rate limit hit for resident {ResidentId} ({Count} in {Minutes}m) — no new code",
                resident.Id, recentCount, _challengeWindowMinutes);
            await SendAsync(channel, identifier, BuildRateLimitMessage(resident.PreferredLanguage), cancellationToken);
            return OtpSendStatus.RateLimited;
        }

        var code = GenerateCode();
        var challenge = OtpChallenge.Create(
            identifier: identifier,
            channel: channel,
            codeHash: HashCode(code),
            expiresAt: now.AddMinutes(_expiryMinutes));

        await _challenges.AddAsync(challenge, cancellationToken);
        await _challenges.SaveChangesAsync(cancellationToken);

        await SendAsync(channel, identifier, BuildCodeMessage(code, resident.PreferredLanguage), cancellationToken);

        _logger.LogInformation(
            "OTP issued and sent to resident {ResidentId} over {Channel} (challenge {ChallengeId})",
            resident.Id, channel, challenge.Id);

        return OtpSendStatus.Sent;
    }

    public async Task<OtpVerifyResult> VerifyAsync(string code, CancellationToken cancellationToken = default)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return OtpVerifyResult.Invalid;

        var matches = await _challenges.FindUnconsumedByCodeHashAsync(HashCode(trimmed), cancellationToken);
        if (matches.Count == 0)
            return OtpVerifyResult.Invalid;

        var now = DateTime.UtcNow;
        var redeemable = matches.Where(c => c.IsRedeemable(now, _maxAttempts)).ToList();

        if (redeemable.Count == 0)
            return OtpVerifyResult.Expired;

        // Multiple active challenges sharing one code: reject rather than guess which resident.
        if (redeemable.Count > 1)
        {
            _logger.LogWarning(
                "OTP verify collision: {Count} active challenges share a code — rejected", redeemable.Count);
            return OtpVerifyResult.TooManyMatches;
        }

        var challenge = redeemable[0];

        var resident = await ResolveResidentAsync(challenge.Identifier, challenge.Channel, cancellationToken);
        if (resident is null || !IsAuthorizedForLogin(resident))
        {
            // Role revoked or resident deactivated between issue and verify — do not consume; let it expire.
            _logger.LogWarning(
                "OTP matched challenge {ChallengeId} but resident is no longer authorized — login refused",
                challenge.Id);
            return OtpVerifyResult.Invalid;
        }

        challenge.Consume();
        await _challenges.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "OTP verified — resident {ResidentId} signed in (challenge {ChallengeId})",
            resident.Id, challenge.Id);

        return OtpVerifyResult.Success(resident);
    }

    private Task<Resident?> ResolveResidentAsync(string identifier, ChannelType channel, CancellationToken ct)
        => channel switch
        {
            ChannelType.Telegram => long.TryParse(identifier, out var tgId)
                ? _residents.FindByTelegramIdAsync(tgId, ct)
                : Task.FromResult<Resident?>(null),
            ChannelType.WhatsApp => _residents.FindByWhatsAppNumberAsync(identifier, ct),
            _ => Task.FromResult<Resident?>(null)
        };

    private Task SendAsync(ChannelType channel, string recipientId, string text, CancellationToken ct)
    {
        var adapter = channel switch
        {
            ChannelType.Telegram => _telegram,
            ChannelType.WhatsApp => _whatsApp,
            _ => throw new NotSupportedException($"OTP delivery not supported for channel {channel}.")
        };
        return adapter.SendAsync(recipientId, text, ct);
    }

    /// <summary>Authorized to log in: active resident with any assigned role (≥ Manager). Plain residents (None) cannot.</summary>
    private static bool IsAuthorizedForLogin(Resident resident)
        => resident.IsActive && resident.Role >= ResidentRole.Manager;

    /// <summary>6-digit, cryptographically random, uniform over [000000, 999999].</summary>
    private static string GenerateCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>SHA-256 hex of the code. Deterministic so the verify path can look up by hash.</summary>
    private static string HashCode(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private string BuildCodeMessage(string code, string lang) => lang == "en"
        ? $"🔐 Your Hanwas login code: {code} (valid {_expiryMinutes} min)"
        : $"🔐 Hanwas giriş kodunuz: {code} ({_expiryMinutes} dakika geçerli)";

    private static string BuildRateLimitMessage(string lang) => lang == "en"
        ? "⚠️ Too many login requests. Please wait a few minutes and try again."
        : "⚠️ Çok fazla giriş isteği. Lütfen birkaç dakika sonra tekrar deneyin.";
}
