using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Infrastructure.Persistence;

public sealed class ApartmentTriageDbContext : DbContext
{
    public ApartmentTriageDbContext(DbContextOptions<ApartmentTriageDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations will be applied from this assembly via reflection
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApartmentTriageDbContext).Assembly);
    }
}
