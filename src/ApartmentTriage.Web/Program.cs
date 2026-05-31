using ApartmentTriage.Application;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Infrastructure.Services;
using ApartmentTriage.Web.Endpoints;
using ApartmentTriage.Web.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.Dashboard;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
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

    // ── Auth (GRUP C) ──────────────────────────────────────────────────────────
    // Auth:Enabled is the demo safety net. When false, authorization is not wired at
    // all (every page/API open) — used for the first deploy until bootstrap + live
    // login are verified, and for local dev while the login pages (GRUP B) don't exist.
    var authEnabled = builder.Configuration.GetValue("Auth:Enabled", true);

    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.AccessDeniedPath = "/login";   // GRUP B: friendly "panel atanmadı" message
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Cookie.Name = "hanwas_auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // GRUP D hardening will force CookieSecurePolicy.Always for prod;
            // SameAsRequest keeps local http dev working in the meantime.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

    // Role-ready policy: today Manager only; future roles extend via RequireRole(...).
    builder.Services.AddAuthorization(options =>
        options.AddPolicy("DashboardAccess", policy =>
            policy.RequireRole(nameof(ResidentRole.Manager))));

    // Razor Pages (dashboard UI) — protected as a folder unless the flag is off.
    builder.Services.AddRazorPages(options =>
    {
        if (!authEnabled) return;
        options.Conventions.AuthorizeFolder("/", "DashboardAccess");
        options.Conventions.AllowAnonymousToPage("/Login");
        options.Conventions.AllowAnonymousToPage("/Logout");
    });

    // Telegram consumer: run as a simple background service instead of Hangfire recurring job.
    // This avoids distributed lock issues in local development and keeps polling reliable.
    builder.Services.AddTransient<ChannelConsumerJob>();
    builder.Services.AddTransient<WhatsAppConsumerJob>();
    builder.Services.AddHostedService<TelegramConsumerHostedService>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
        app.UseDeveloperExceptionPage();

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = Array.Empty<IDashboardAuthorizationFilter>()
    });

    // Remove stale Telegram recurring jobs from previous deployments.
    // The app now uses a hosted service for Telegram long polling.
    var recurringJobs = JobStorage.Current.GetConnection().GetRecurringJobs();
    foreach (var recurringJob in recurringJobs)
    {
        if (recurringJob.Job?.Type == typeof(ChannelConsumerJob) &&
            recurringJob.Job.Method.Name == nameof(ChannelConsumerJob.RunAsync))
        {
            RecurringJob.RemoveIfExists(recurringJob.Id);
            Log.Information("Removed stale Hangfire recurring job '{JobId}' for ChannelConsumerJob.RunAsync", recurringJob.Id);
        }
    }

    var monitoringApi = JobStorage.Current.GetMonitoringApi();
    var allQueueNames = monitoringApi.Queues().Select(q => q.Name).Distinct();
    foreach (var queueName in allQueueNames)
    {
        foreach (var enqueuedJob in monitoringApi.EnqueuedJobs(queueName, 0, int.MaxValue))
        {
            if (IsChannelConsumerJob(enqueuedJob.Value.Job))
            {
                BackgroundJob.Delete(enqueuedJob.Key);
                Log.Information("Deleted stale Hangfire enqueued job '{JobId}' from queue '{Queue}'", enqueuedJob.Key, queueName);
            }
        }
    }

    foreach (var processingJob in monitoringApi.ProcessingJobs(0, int.MaxValue))
    {
        if (IsChannelConsumerJob(processingJob.Value.Job))
        {
            BackgroundJob.Delete(processingJob.Key);
            Log.Information("Deleted stale Hangfire processing job '{JobId}'", processingJob.Key);
        }
    }

    foreach (var scheduledJob in monitoringApi.ScheduledJobs(0, int.MaxValue))
    {
        if (IsChannelConsumerJob(scheduledJob.Value.Job))
        {
            BackgroundJob.Delete(scheduledJob.Key);
            Log.Information("Deleted stale Hangfire scheduled job '{JobId}'", scheduledJob.Key);
        }
    }

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/", () => Results.Redirect("/overview"));
    app.MapRazorPages();

    // Dashboard data/mutation APIs — gated behind the same policy as the pages when auth
    // is enabled (otherwise the page lock would be bypassable via /api/*). The WhatsApp
    // webhook and /health stay anonymous (mapped on `app` below).
    var dashboardApi = app.MapGroup("");
    if (authEnabled)
        dashboardApi.RequireAuthorization("DashboardAccess");
    dashboardApi.MapStatsEndpoints();
    dashboardApi.MapReplyEndpoints();
    dashboardApi.MapResidentEndpoints();
    dashboardApi.MapTicketEndpoints();

    static bool IsChannelConsumerJob(Job? job)
    {
        return job?.Type == typeof(ChannelConsumerJob) &&
               job.Method.Name == nameof(ChannelConsumerJob.RunAsync);
    }
    app.MapWhatsAppWebhook();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    // WhatsApp consumer: same 1-minute CRON, 10s drain window (push-based, no long-poll).
    RecurringJob.AddOrUpdate<WhatsAppConsumerJob>(
        recurringJobId: "whatsapp-consumer",
        methodCall: job => job.RunAsync(CancellationToken.None),
        cronExpression: Cron.Minutely());

    // Apply pending EF Core migrations on startup. Boot fails (Fly rollback) if migration errors.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApartmentTriageDbContext>();
        await db.Database.MigrateAsync();

        // Bootstrap the first Manager (idempotent; no-op until the resident exists).
        var bootstrapper = scope.ServiceProvider.GetRequiredService<ManagerBootstrapper>();
        await bootstrapper.RunAsync();
    }

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
