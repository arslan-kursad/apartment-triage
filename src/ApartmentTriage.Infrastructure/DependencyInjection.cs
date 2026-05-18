using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Infrastructure.Persistence.Repositories;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentTriage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // EF Core + Postgres + pgvector + snake_case
        services.AddDbContext<ApartmentTriageDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.UseVector())
                .UseSnakeCaseNamingConvention());

        services.AddScoped<ITicketRepository, TicketRepository>();

        // Hangfire — PostgreSQL storage (no Redis)
        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer();

        return services;
    }
}
