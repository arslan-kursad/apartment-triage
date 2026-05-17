using ApartmentTriage.Infrastructure;
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

    // Razor Pages (dashboard UI)
    builder.Services.AddRazorPages();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
        app.UseDeveloperExceptionPage();

    app.UseHangfireDashboard("/hangfire");
    app.MapRazorPages();

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

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
