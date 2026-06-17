using ApartmentTriage.Application.Agents.Anthropic;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Embeddings;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Application.Services;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Anthropic;
using ApartmentTriage.Infrastructure.Channels;
using ApartmentTriage.Infrastructure.Embeddings;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Infrastructure.Persistence.Repositories;
using ApartmentTriage.Infrastructure.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector.Npgsql;
using Telegram.Bot;

namespace ApartmentTriage.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // EF Core + Postgres + pgvector + snake_case.
        // Register the vector type on the ADO.NET data source (not just EF) so that raw
        // SqlQueryRaw vector parameters — TicketRepository.FindSimilarAsync — can serialize.
        // EF's UseVector() alone only wires LINQ mappings, not the raw-parameter serializer.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ApartmentTriageDbContext>(options =>
            options
                .UseNpgsql(
                    dataSource,
                    npgsql => npgsql.UseVector())
                .UseSnakeCaseNamingConvention());

        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IResidentRepository, ResidentRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IOtpChallengeRepository, OtpChallengeRepository>();
        services.AddScoped<IAnonymizationService, AnonymizationService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ManagerBootstrapper>();
        services.AddSingleton<IModelCostCalculator, ModelCostCalculator>();

        // Hangfire — PostgreSQL storage (no Redis)
        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer();

        return services;
    }

    public static IServiceCollection AddWhatsAppChannel(this IServiceCollection services)
    {
        services.AddHttpClient("whatsapp");

        // Single instance registered under two keys:
        //   IMessageChannel (ChannelType.WhatsApp) — consumed by ChannelConsumerJob
        //   WhatsAppAdapter (ChannelType.WhatsApp)  — consumed by webhook endpoint (TryEnqueue)
        services.AddKeyedSingleton<WhatsAppAdapter>(ChannelType.WhatsApp);
        services.AddKeyedSingleton<IMessageChannel>(
            ChannelType.WhatsApp,
            (sp, key) => sp.GetRequiredKeyedService<WhatsAppAdapter>((ChannelType)key!));
        return services;
    }

    public static IServiceCollection AddTelegramChannel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var token = configuration["TelegramBot:Token"]?.Trim()
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

        // Single instance registered under two keys (mirrors AddWhatsAppChannel):
        //   IMessageChannel (ChannelType.Telegram) — consumed by ChannelConsumerJob (drain)
        //   TelegramAdapter (ChannelType.Telegram)  — consumed by webhook endpoint (TryEnqueue)
        services.AddKeyedSingleton<TelegramAdapter>(ChannelType.Telegram);
        services.AddKeyedSingleton<IMessageChannel>(
            ChannelType.Telegram,
            (sp, key) => sp.GetRequiredKeyedService<TelegramAdapter>((ChannelType)key!));

        return services;
    }

    public static IServiceCollection AddEmbeddings(
        this IServiceCollection services,
        IConfiguration configuration,
        bool allowFallback = false)
    {
        var modelPath = configuration["Embeddings:ModelPath"]
            ?? throw new InvalidOperationException(
                "Embeddings:ModelPath not found. " +
                "Set via user-secrets or EMBEDDINGS__MODELPATH env var. " +
                "Run scripts/download-models.sh to download the model.");

        // Singleton: InferenceSession is thread-safe and expensive to initialize.
        // DI container disposes OnnxEmbeddingService (IDisposable) on app shutdown.
        services.AddSingleton<IEmbeddingService>(_ =>
        {
            try
            {
                return new OnnxEmbeddingService(modelPath);
            }
            catch when (allowFallback)
            {
                return new NoopEmbeddingService();
            }
        });

        return services;
    }
}
