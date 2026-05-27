using ApartmentTriage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApartmentTriage.Infrastructure.Persistence.Configurations;

internal sealed class ResidentConfiguration : IEntityTypeConfiguration<Resident>
{
    public void Configure(EntityTypeBuilder<Resident> b)
    {
        b.HasKey(r => r.Id);

        b.Property(r => r.ApartmentNumber)
            .HasMaxLength(20);

        b.Property(r => r.DisplayName)
            .HasMaxLength(100);

        b.Property(r => r.WhatsAppNumber)
            .HasMaxLength(20);

        b.Property(r => r.PreferredLanguage)
            .HasMaxLength(5)
            .HasDefaultValue("tr")
            .IsRequired();

        b.Property(r => r.CreatedAt)
            .IsRequired();

        // Unique constraint: one resident per WhatsApp number
        b.HasIndex(r => r.WhatsAppNumber)
            .IsUnique()
            .HasFilter("whats_app_number IS NOT NULL");

        // Unique constraint: one resident per Telegram ID
        b.HasIndex(r => r.TelegramId)
            .IsUnique()
            .HasFilter("telegram_id IS NOT NULL");
    }
}
