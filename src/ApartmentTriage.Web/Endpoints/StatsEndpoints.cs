using System.Globalization;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Web.Helpers;
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
        app.MapGet("/api/stats/confidence", GetConfidence);
        app.MapGet("/api/stats/ticket-status", GetTicketStatus);
        app.MapGet("/api/stats/hourly", GetHourly);
        app.MapGet("/api/stats/trends", GetTrends);
        app.MapGet("/api/tickets/recent", GetRecentTickets);
        app.MapGet("/api/eval/summary", GetEvalSummary);
    }

    // Demo realism (D2): dev/test Mock-channel tickets never count toward dashboard
    // stats. Real channel identity lives on the source message, so we filter there.
    private static IQueryable<Ticket> NonMockTickets(ApartmentTriageDbContext db)
        => db.Tickets.Where(t => t.SourceMessage!.ChannelType != ChannelType.Mock);

    private static async Task<IResult> GetStats(ApartmentTriageDbContext db)
    {
        var tickets = NonMockTickets(db);

        var total     = await tickets.CountAsync();
        var emergency = await tickets.CountAsync(t => t.IsEmergency);

        var channelCounts = await tickets
            .GroupBy(t => t.SourceMessage!.ChannelType)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToListAsync();

        int waCount = channelCounts.FirstOrDefault(x => x.Channel == ChannelType.WhatsApp)?.Count ?? 0;
        int tgCount = channelCounts.FirstOrDefault(x => x.Channel == ChannelType.Telegram)?.Count ?? 0;

        int sonnetCount = await tickets.CountAsync(t => t.EscalatedToSonnet);
        int haikuCount  = total - sonnetCount;

        // ── Operations KPIs ──────────────────────────────────────────────────
        // Resident counts mirror the /residents page (all residents by IsActive) so
        // the Overview KPI and the Residents table never disagree during the demo.
        int activeResidents = await db.Residents.CountAsync(r => r.IsActive);
        int totalResidents  = await db.Residents.CountAsync();
        int totalMessages   = await db.Messages.CountAsync(m => m.ChannelType != ChannelType.Mock);

        // Daily average over distinct active days (Istanbul calendar date).
        var createdUtc = await tickets.Select(t => t.CreatedAt).ToListAsync();
        int activeDays = createdUtc.Select(u => IstanbulTime.FromUtc(u).Date).Distinct().Count();
        double dailyAvgTickets = activeDays > 0 ? Math.Round((double)total / activeDays, 1) : 0;

        return Results.Ok(new
        {
            total,
            emergency,
            waCount,
            tgCount,
            haikuCount,
            sonnetCount,
            activeResidents,
            totalResidents,
            totalMessages,
            activeDays,
            dailyAvgTickets
        });
    }

    private static async Task<IResult> GetCategories(ApartmentTriageDbContext db)
    {
        var data = await NonMockTickets(db)
            .GroupBy(t => t.Category)
            .Select(g => new { category = g.Key.ToString(), count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        return Results.Ok(data);
    }

    private static async Task<IResult> GetSeverity(ApartmentTriageDbContext db)
    {
        var data = await NonMockTickets(db)
            .GroupBy(t => t.Severity)
            .Select(g => new { severity = g.Key.ToString(), count = g.Count() })
            .ToListAsync();

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
        var data = await NonMockTickets(db)
            .Where(t => t.RoutingAction != null)
            .GroupBy(t => t.RoutingAction!.Value)
            .Select(g => new { action = g.Key.ToString(), count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        return Results.Ok(data);
    }

    // Category-confidence distribution (High/Medium/Low) — replaces the former
    // fabricated "average confidence %" KPI. Canonical High→Medium→Low order,
    // zero-filled so the donut/legend is stable.
    private static async Task<IResult> GetConfidence(ApartmentTriageDbContext db)
    {
        var raw = await NonMockTickets(db)
            .GroupBy(t => t.CategoryConfidence)
            .Select(g => new { level = g.Key, count = g.Count() })
            .ToListAsync();

        var order = new[] { ConfidenceLevel.High, ConfidenceLevel.Medium, ConfidenceLevel.Low };
        var result = order.Select(l => new
        {
            level = l.ToString(),
            count = raw.FirstOrDefault(x => x.level == l)?.count ?? 0
        }).ToList();

        return Results.Ok(result);
    }

    // Ticket status distribution (Kürşad's request). Canonical lifecycle order;
    // only statuses that actually occur are returned ("sadece var olanları göster").
    private static async Task<IResult> GetTicketStatus(ApartmentTriageDbContext db)
    {
        var raw = await NonMockTickets(db)
            .GroupBy(t => t.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync();

        var order = new[] { TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed };
        var result = order
            .Select(s => new { status = s.ToString(), count = raw.FirstOrDefault(x => x.status == s)?.count ?? 0 })
            .Where(x => x.count > 0)
            .ToList();

        return Results.Ok(result);
    }

    // Hour-of-day intensity, bucketed in Istanbul local time (UTC+3). 24 buckets,
    // zero-filled, so the bar chart always spans 00–23.
    private static async Task<IResult> GetHourly(ApartmentTriageDbContext db)
    {
        var times = await NonMockTickets(db)
            .Select(t => t.CreatedAt)
            .ToListAsync();

        var buckets = new int[24];
        foreach (var utc in times)
            buckets[IstanbulTime.FromUtc(utc).Hour]++;

        var result = Enumerable.Range(0, 24)
            .Select(h => new { hour = h, label = h.ToString("00", CultureInfo.InvariantCulture), count = buckets[h] })
            .ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> GetTrends(ApartmentTriageDbContext db)
    {
        var sinceUtc = DateTime.UtcNow.AddDays(-14);

        var times = await NonMockTickets(db)
            .Where(t => t.CreatedAt >= sinceUtc)
            .Select(t => t.CreatedAt)
            .ToListAsync();

        // Group by Istanbul calendar date (consistent with the hourly endpoint).
        var byDay = times
            .GroupBy(u => IstanbulTime.FromUtc(u).Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var todayIst = IstanbulTime.FromUtc(DateTime.UtcNow).Date;

        var result = new List<object>();
        for (int i = 13; i >= 0; i--)
        {
            var day = todayIst.AddDays(-i);
            result.Add(new
            {
                date = day.ToString("dd MMM", CultureInfo.InvariantCulture),
                count = byDay.TryGetValue(day, out var c) ? c : 0
            });
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GetRecentTickets(ApartmentTriageDbContext db)
    {
        var tickets = await NonMockTickets(db)
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
            haikuSonnetRatioAvailable = true,
            evalCostEstimateUsd = section.GetValue<double?>("EvalCostEstimateUsd"),
            totalApiCostUsd = config.GetValue<string>("Dashboard:TotalApiCostUsd")
        });
    }

    private static double? ParseNullableDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
}
