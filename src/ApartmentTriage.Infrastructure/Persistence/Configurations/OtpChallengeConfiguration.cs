using ApartmentTriage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApartmentTriage.Infrastructure.Persistence.Configurations;

internal sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> b)
    {
        b.HasKey(c => c.Id);

        b.Property(c => c.Identifier)
            .HasMaxLength(64)
            .IsRequired();

        b.Property(c => c.Channel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // SHA-256 hex = 64 chars.
        b.Property(c => c.CodeHash)
            .HasMaxLength(64)
            .IsRequired();

        b.Property(c => c.ExpiresAt).IsRequired();
        b.Property(c => c.AttemptCount).IsRequired();
        b.Property(c => c.ConsumedAt).IsRequired(false);
        b.Property(c => c.CreatedAt).IsRequired();

        // Verify path: look up redeemable challenges by code hash.
        b.HasIndex(c => c.CodeHash);

        // Rate-limit path: count recent challenges per identifier + channel.
        b.HasIndex(c => new { c.Identifier, c.Channel, c.CreatedAt });
    }
}
