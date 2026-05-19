using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Embeddings;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Channels;
using ApartmentTriage.Infrastructure.Embeddings;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Infrastructure.Persistence.Repositories;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

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

    public static IServiceCollection AddTelegramChannel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var token = configuration["TelegramBot:Token"]
            ?? throw new InvalidOperationException(
                "TelegramBot:Token not configured. " +
                "Run: dotnet user-secrets set \"TelegramBot:Token\" \"<token>\"");

        // Singleton bot client via IHttpClientFactory — avoids socket exhaustion.
        services.AddHttpClient("telegram");
        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new TelegramBotClient(token, factory.CreateClient("telegram"));
        });

        services.AddKeyedSingleton<IMessageChannel, TelegramAdapter>(ChannelType.Telegram);

        return services;
    }

    public static IServiceCollection AddEmbeddings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var modelPath = configuration["Embeddings:ModelPath"]
            ?? throw new InvalidOperationException(
                "Embeddings:ModelPath not found. " +
                "Set via user-secrets or EMBEDDINGS__MODELPATH env var. " +
                "Run scripts/download-models.sh to download the model.");

        // Singleton: InferenceSession is thread-safe and expensive to initialize.
        // DI container disposes OnnxEmbeddingService (IDisposable) on app shutdown.
        services.AddSingleton<IEmbeddingService>(_ => new OnnxEmbeddingService(modelPath));

        return services;
    }
}
