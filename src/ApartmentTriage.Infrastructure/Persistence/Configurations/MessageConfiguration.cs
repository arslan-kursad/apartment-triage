using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApartmentTriage.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.HasKey(m => m.Id);

        b.Property(m => m.ChannelType)
            .IsRequired()
            .HasConversion<string>();

        b.Property(m => m.ExternalMessageId)
            .IsRequired();

        // Idempotency: same external message must not be processed twice
        b.HasIndex(m => new { m.ChannelType, m.ExternalMessageId })
            .IsUnique();

        b.Property(m => m.RawText)
            .IsRequired();

        b.Property(m => m.MatchedPhrases)
            .HasColumnType("text[]");

        b.Property(m => m.ReceivedAt)
            .IsRequired();

        b.HasOne(m => m.Resident)
            .WithMany(r => r.Messages)
            .HasForeignKey(m => m.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(m => m.ResidentId);
        b.HasIndex(m => m.ReceivedAt);
    }
}
