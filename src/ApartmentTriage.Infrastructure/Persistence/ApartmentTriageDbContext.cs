using ApartmentTriage.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Infrastructure.Persistence;

public sealed class ApartmentTriageDbContext : DbContext
{
    public ApartmentTriageDbContext(DbContextOptions<ApartmentTriageDbContext> options)
        : base(options) { }

    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<ApiUsageRecord> ApiUsageRecords => Set<ApiUsageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Picks up all IEntityTypeConfiguration<T> in this assembly automatically
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApartmentTriageDbContext).Assembly);
    }
}
