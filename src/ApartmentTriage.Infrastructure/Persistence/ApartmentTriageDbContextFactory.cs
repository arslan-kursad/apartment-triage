using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ApartmentTriage.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations add/update).
/// Bypasses Program.cs startup — no secrets, no ONNX, no Hangfire needed.
/// Connection string is irrelevant at migration generation time; a dummy value is sufficient.
/// </summary>
internal sealed class ApartmentTriageDbContextFactory
    : IDesignTimeDbContextFactory<ApartmentTriageDbContext>
{
    public ApartmentTriageDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApartmentTriageDbContext>();
        optionsBuilder
            .UseNpgsql(
                "Host=localhost;Database=design_time;Username=postgres",
                npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention();

        return new ApartmentTriageDbContext(optionsBuilder.Options);
    }
}
