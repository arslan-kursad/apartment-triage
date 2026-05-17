using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApartmentTriage.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> b)
    {
        b.HasKey(t => t.Id);

        b.Property(t => t.Category)
            .IsRequired()
            .HasConversion<string>();

        b.Property(t => t.Severity)
            .IsRequired()
            .HasConversion<string>();

        b.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>();

        b.Property(t => t.CategoryConfidence)
            .IsRequired()
            .HasConversion<string>();

        b.Property(t => t.EmergencyConfidence)
            .IsRequired()
            .HasConversion<string>();

        b.Property(t => t.LocationHint)
            .HasMaxLength(100);

        // Context: cause-effect data per taxonomy.v3.yaml § multi_issue.causal_relation_spec
        // Notes: operational follow-up (technician, manager). Separate by intent.
        b.Property(t => t.Context)
            .HasMaxLength(2000);

        b.Property(t => t.Notes)
            .HasMaxLength(2000);

        b.Property(t => t.CreatedAt).IsRequired();
        b.Property(t => t.UpdatedAt).IsRequired();

        b.HasOne(t => t.Resident)
            .WithMany(r => r.Tickets)
            .HasForeignKey(t => t.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(t => t.SourceMessage)
            .WithMany()
            .HasForeignKey(t => t.SourceMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(t => t.ResidentId);
        b.HasIndex(t => t.Status);
        b.HasIndex(t => t.Category);
        b.HasIndex(t => new { t.IsEmergency, t.Status });
        b.HasIndex(t => t.CreatedAt);
    }
}
