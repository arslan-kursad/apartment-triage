using ApartmentTriage.Application;
using ApartmentTriage.Infrastructure;
using ApartmentTriage.Web.Endpoints;
using ApartmentTriage.Web.Jobs;
using Hangfire;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog — structured JSON in production, pretty console in dev
    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .WriteTo.Console(new CompactJsonFormatter()));

    // Infrastructure — EF Core + Postgres + Hangfire storage (all in one call)
    var connectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Connection string 'Default' not found. " +
            "Run: dotnet user-secrets set \"ConnectionStrings:Default\" \"<connstring>\"");

    builder.Services.AddInfrastructure(connectionString);

    // ONNX embedding service — required for EnricherAgent (multilingual-e5-small).
    // Run scripts/download-models.sh then set: dotnet user-secrets set "Embeddings:ModelPath" "<path>"
    builder.Services.AddEmbeddings(
        builder.Configuration,
        allowFallback: builder.Environment.IsDevelopment());

    // Anthropic API client
    var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"]
        ?? throw new InvalidOperationException(
            "Anthropic:ApiKey not configured. " +
            "Run: dotnet user-secrets set \"Anthropic:ApiKey\" \"<key>\"");

    builder.Services.AddAnthropicClient(anthropicApiKey);

    // Application layer — triage pipeline, orchestrator, agents
    builder.Services.AddApplication();

    // WhatsApp channel (Singleton keyed adapter, webhook → BoundedChannel)
    builder.Services.AddWhatsAppChannel();

    // Telegram channel (Singleton keyed adapter + ITelegramBotClient)
    builder.Services.AddTelegramChannel(builder.Configuration);

    // Razor Pages (dashboard UI)
    builder.Services.AddRazorPages();

    // Telegram consumer: run as a simple background service instead of Hangfire recurring job.
    // This avoids distributed lock issues in local development and keeps polling reliable.
    builder.Services.AddTransient<ChannelConsumerJob>();
    builder.Services.AddHostedService<TelegramConsumerHostedService>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
        app.UseDeveloperExceptionPage();

    app.UseHangfireDashboard("/hangfire");
    app.MapRazorPages();
    app.MapWhatsAppWebhook();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    // WhatsApp consumer: same 1-minute CRON, 10s drain window (push-based, no long-poll).
    RecurringJob.AddOrUpdate<WhatsAppConsumerJob>(
        recurringJobId: "whatsapp-consumer",
        methodCall: job => job.RunAsync(CancellationToken.None),
        cronExpression: Cron.Minutely());

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
