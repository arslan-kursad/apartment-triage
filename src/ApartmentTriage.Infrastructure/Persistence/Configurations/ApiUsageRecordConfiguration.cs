using ApartmentTriage.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApartmentTriage.Infrastructure.Persistence.Configurations;

internal sealed class ApiUsageRecordConfiguration : IEntityTypeConfiguration<ApiUsageRecord>
{
    public void Configure(EntityTypeBuilder<ApiUsageRecord> b)
    {
        b.HasKey(r => r.Id);

        b.Property(r => r.AgentId).HasMaxLength(40).IsRequired();
        b.Property(r => r.Model).HasMaxLength(80).IsRequired();
        b.Property(r => r.InputTokens).IsRequired();
        b.Property(r => r.OutputTokens).IsRequired();
        b.Property(r => r.CacheReadTokens).IsRequired();
        b.Property(r => r.CacheCreationTokens).IsRequired();
        b.Property(r => r.CreatedAt).IsRequired();

        // Dashboard queries group by day, model, and agent.
        b.HasIndex(r => r.CreatedAt);
        b.HasIndex(r => new { r.Model, r.CreatedAt });
    }
}
