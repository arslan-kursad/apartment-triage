using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Repositories;

public interface IOtpChallengeRepository
{
    Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts challenges created for this identifier+channel at or after <paramref name="since"/>.
    /// Backs the per-identifier challenge rate limit (regardless of consumed/expired state).
    /// </summary>
    Task<int> CountSinceAsync(
        string identifier,
        ChannelType channel,
        DateTime since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns unconsumed challenges whose code hash matches. The service applies expiry,
    /// attempt-ceiling, and collision logic. Bounded set in practice (short-lived codes).
    /// </summary>
    Task<IReadOnlyList<OtpChallenge>> FindUnconsumedByCodeHashAsync(
        string codeHash,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
