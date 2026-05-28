using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ApartmentTriage.Web.Endpoints;

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", GetStats);
        app.MapGet("/api/stats/categories", GetCategories);
        app.MapGet("/api/stats/severity", GetSeverity);
        app.MapGet("/api/stats/routing", GetRouting);
        app.MapGet("/api/stats/trends", GetTrends);
        app.MapGet("/api/tickets/recent", GetRecentTickets);
        app.MapGet("/api/eval/summary", GetEvalSummary);
    }

    private static async Task<IResult> GetStats(ApartmentTriageDbContext db)
    {
        var total = await db.Tickets.CountAsync();
        var emergency = await db.Tickets.CountAsync(t => t.IsEmergency);

        var confidenceInts = await db.Tickets
            .Select(t => (int)t.CategoryConfidence)
            .ToListAsync();

        double avgConfidence = confidenceInts.Count > 0
            ? Math.Round(confidenceInts.Average() / 2.0 * 100.0, 1)
            : 0;

        var channelCounts = await db.Tickets
            .Join(db.Messages,
                t => t.SourceMessageId,
                m => m.Id,
                (t, m) => m.ChannelType)
            .GroupBy(ct => ct)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToListAsync();

        int waCount = channelCounts.FirstOrDefault(x => x.Channel == ChannelType.WhatsApp)?.Count ?? 0;
        int tgCount = channelCounts.FirstOrDefault(x => x.Channel == ChannelType.Telegram)?.Count ?? 0;

        return Results.Ok(new
        {
            total,
            emergency,
            avgConfidence,
            waCount,
            tgCount
        });
    }

    private static async Task<IResult> GetCategories(ApartmentTriageDbContext db)
    {
        var data = await db.Tickets
            .GroupBy(t => t.Category)
            .Select(g => new { category = g.Key.ToString(), count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        return Results.Ok(data);
    }

    private static async Task<IResult> GetSeverity(ApartmentTriageDbContext db)
    {
        var data = await db.Tickets
            .GroupBy(t => t.Severity)
            .Select(g => new { severity = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

        // Sort by severity level
        var ordered = data
            .OrderBy(x => x.severity switch
            {
                "Low"    => 0,
                "Medium" => 1,
                "High"   => 2,
                "Urgent" => 3,
                _        => 4
            })
            .ToList();

        return Results.Ok(ordered);
    }

    private static async Task<IResult> GetRouting(ApartmentTriageDbContext db)
    {
        var data = await db.Tickets
            .Where(t => t.RoutingAction != null)
            .GroupBy(t => t.RoutingAction!.Value)
            .Select(g => new { action = g.Key.ToString(), count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        return Results.Ok(data);
    }

    private static async Task<IResult> GetTrends(ApartmentTriageDbContext db)
    {
        var since = DateTime.UtcNow.AddDays(-14);

        var data = await db.Tickets
            .Where(t => t.CreatedAt >= since)
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(x => x.date)
            .ToListAsync();

        // Fill in missing days with 0
        var result = new List<object>();
        for (int i = 13; i >= 0; i--)
        {
            var day = DateTime.UtcNow.Date.AddDays(-i);
            var found = data.FirstOrDefault(d => d.date == day);
            result.Add(new
            {
                date = day.ToString("dd MMM"),
                count = found?.count ?? 0
            });
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GetRecentTickets(ApartmentTriageDbContext db)
    {
        var tickets = await db.Tickets
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToListAsync();

        var result = tickets.Select(t => new
        {
            id = t.Id,
            category = t.Category.ToString(),
            severity = t.Severity.ToString(),
            isEmergency = t.IsEmergency,
            channel = t.SourceMessage?.ChannelType.ToString() ?? "Unknown",
            preview = t.SourceMessage?.RawText is { Length: > 0 } raw
                ? (raw.Length > 60 ? raw[..60] + "…" : raw)
                : "—",
            resident = t.Resident?.DisplayName
                ?? t.Resident?.ApartmentNumber
                ?? t.ResidentId.ToString()[..8] + "…",
            createdAtIst = t.CreatedAt.ToString("dd MMM HH:mm")  // caller converts if needed
        }).ToList();

        return Results.Ok(result);
    }

    private static IResult GetEvalSummary(IConfiguration config)
    {
        // Values updated manually after each eval run via appsettings.json
        var section = config.GetSection("Dashboard:Eval");

        var categoryAccuracy  = section["CategoryAccuracy"];
        var emergencyRecall   = section["EmergencyRecall"];
        var emergencyPrecision = section["EmergencyPrecision"];
        var totalCases        = section.GetValue<int?>("TotalCases");
        var runDate           = section["RunDate"];

        return Results.Ok(new
        {
            categoryAccuracy  = ParseNullableDouble(categoryAccuracy),
            emergencyRecall   = ParseNullableDouble(emergencyRecall),
            emergencyPrecision = ParseNullableDouble(emergencyPrecision),
            totalCases        = totalCases ?? 48,
            runDate,
            // FinOps — Haiku/Sonnet ratio requires schema migration (ADR-0012 §Açık Sorunlar)
            haikuSonnetRatioAvailable = false,
            evalCostEstimateUsd = section.GetValue<double?>("EvalCostEstimateUsd"),
            totalApiCostUsd = config.GetValue<string>("Dashboard:TotalApiCostUsd")
        });
    }

    private static double? ParseNullableDouble(string? value)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
}
