using ApartmentTriage.Application;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Infrastructure.Services;
using ApartmentTriage.Web.Endpoints;
using ApartmentTriage.Web.Jobs;
using ApartmentTriage.Web.Security;
using Hangfire;
using Hangfire.Common;
using Hangfire.Dashboard;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
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
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.Redirect("/login?reason=denied");
                return Task.CompletedTask;
            };
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Cookie.Name = "hanwas_auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

    // Role-ready policy: today Manager only; future roles extend via RequireRole(...).
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("DashboardAccess", policy => policy.RequireAuthenticatedUser());
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
    // Reverse proxy header configuration for Fly.io compatibility
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Razor Pages (dashboard UI) — protected as a folder unless the flag is off.
    builder.Services.AddRazorPages(options =>
    {
        if (!authEnabled) return;
        options.Conventions.AuthorizeFolder("/", "DashboardAccess");
        options.Conventions.AllowAnonymousToPage("/Login");
        options.Conventions.AllowAnonymousToPage("/Logout");
    });

    // Channel consumers: register as transient services for Hangfire to orchestrate
    // Hangfire's distributed lock (Postgres-backed) guarantees 1 instance across N machines
    builder.Services.AddTransient<ChannelConsumerJob>();
    builder.Services.AddTransient<WhatsAppConsumerJob>();
    // TelegramConsumerHostedService disabled: use Hangfire recurring job for distributed safety
    builder.Services.AddSingleton<LoginAttemptLimiter>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
        app.UseDeveloperExceptionPage();

    app.UseForwardedHeaders();
    app.UseMiddleware<LegacyHostRedirectMiddleware>();
    app.UseStaticFiles();

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = authEnabled
            ? [new HangfireDashboardAuthorizationFilter()]
            : Array.Empty<IDashboardAuthorizationFilter>()
    });

    var rootApi = app.MapGet("/", () => Results.Redirect("/overview"));
    if (authEnabled)
        rootApi.RequireAuthorization("DashboardAccess");

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


    app.MapWhatsAppWebhook();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    // Apply pending EF Core migrations on startup. Boot fails (Fly rollback) if migration errors.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApartmentTriageDbContext>();
        await db.Database.MigrateAsync();

        // Bootstrap the first Manager (idempotent; no-op until the resident exists).
        var bootstrapper = scope.ServiceProvider.GetRequiredService<ManagerBootstrapper>();
        await bootstrapper.RunAsync();

        // Configure recurring jobs (after migrations, Hangfire storage is ready)
        var residentsList = await db.Residents.ToListAsync();
        foreach (var r in residentsList)
        {
            Log.Information("RESIDENT: Id={Id}, Telegram={Tg}, Role={Role}, Name={Name}",
                r.Id, r.TelegramId, r.Role, r.DisplayName);
        }

        // WhatsApp consumer: 1-minute CRON
        RecurringJob.AddOrUpdate<WhatsAppConsumerJob>(
            recurringJobId: "whatsapp-consumer",
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: Cron.Minutely());

        // Telegram consumer: 1-minute CRON, distributed lock ensures single instance across N machines
        RecurringJob.AddOrUpdate<ChannelConsumerJob>(
            recurringJobId: "telegram-consumer",
            methodCall: job => job.RunAsync(CancellationToken.None),
            cronExpression: Cron.Minutely());

        Log.Information("Recurring jobs configured: whatsapp-consumer, telegram-consumer");
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
