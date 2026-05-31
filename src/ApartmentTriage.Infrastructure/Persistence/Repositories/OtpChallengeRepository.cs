using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Infrastructure.Persistence.Repositories;

internal sealed class OtpChallengeRepository(ApartmentTriageDbContext db) : IOtpChallengeRepository
{
    public Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken = default)
    {
        db.OtpChallenges.Add(challenge);
        return Task.CompletedTask;
    }

    public Task<int> CountSinceAsync(
        string identifier,
        ChannelType channel,
        DateTime since,
        CancellationToken cancellationToken = default)
        => db.OtpChallenges.CountAsync(
            c => c.Identifier == identifier && c.Channel == channel && c.CreatedAt >= since,
            cancellationToken);

    public async Task<IReadOnlyList<OtpChallenge>> FindUnconsumedByCodeHashAsync(
        string codeHash,
        CancellationToken cancellationToken = default)
        => await db.OtpChallenges
            .Where(c => c.CodeHash == codeHash && c.ConsumedAt == null)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
