using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Services;

/// <summary>Outcome of issuing a login code (channel-initiated, e.g. Telegram /login).</summary>
public enum OtpSendStatus
{
    /// <summary>An authorized resident was matched and a code was sent.</summary>
    Sent,

    /// <summary>No resident matched, or the resident lacks dashboard access. No code sent (enumeration-safe).</summary>
    Unauthorized,

    /// <summary>Per-identifier challenge rate limit hit. No new code issued.</summary>
    RateLimited
}

/// <summary>Outcome of verifying a code entered on the web.</summary>
public enum OtpVerifyStatus
{
    Success,

    /// <summary>No redeemable challenge matched the code (wrong, already used, or never issued).</summary>
    Invalid,

    /// <summary>A matching challenge existed but had expired.</summary>
    Expired,

    /// <summary>More than one active challenge shared this code — rejected for safety.</summary>
    TooManyMatches
}

public sealed record OtpVerifyResult(OtpVerifyStatus Status, Resident? Resident)
{
    public static OtpVerifyResult Success(Resident resident) => new(OtpVerifyStatus.Success, resident);
    public static readonly OtpVerifyResult Invalid = new(OtpVerifyStatus.Invalid, null);
    public static readonly OtpVerifyResult Expired = new(OtpVerifyStatus.Expired, null);
    public static readonly OtpVerifyResult TooManyMatches = new(OtpVerifyStatus.TooManyMatches, null);
}

/// <summary>
/// Issues and verifies one-time login codes. Channel-initiated flow: an authorized resident
/// triggers <see cref="GenerateAndSendAsync"/> from their channel (Telegram /login); the code
/// is delivered over that channel and redeemed on the web via <see cref="VerifyAsync"/>.
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// Resolves <paramref name="identifier"/> to a resident on <paramref name="channel"/>, and if the
    /// resident is authorized (Role ≥ Manager, active) and under the rate limit, generates a code,
    /// persists its hash, and sends it over the channel. Enumeration-safe: unauthorized identifiers
    /// receive nothing and the result does not distinguish "unknown" from "not allowed".
    /// </summary>
    Task<OtpSendStatus> GenerateAndSendAsync(
        string identifier,
        ChannelType channel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a code entered on the web. Finds the single redeemable challenge matching the code,
    /// consumes it, and returns the associated resident. Multiple active matches are rejected.
    /// </summary>
    Task<OtpVerifyResult> VerifyAsync(string code, CancellationToken cancellationToken = default);
}
