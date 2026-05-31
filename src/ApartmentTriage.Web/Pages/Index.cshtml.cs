using System.Globalization;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Web.Helpers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly ApartmentTriageDbContext _db;
    private readonly IConfiguration _config;

    public IndexModel(ApartmentTriageDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ── Operations KPIs ───────────────────────────────────────────────────────
    public int    TotalTickets    { get; private set; }
    public int    EmergencyCount  { get; private set; }
    public int    ActiveResidents { get; private set; }
    public int    TotalResidents  { get; private set; }
    public int    TotalMessages   { get; private set; }
    public double DailyAvgTickets { get; private set; }
    public int    WaCount         { get; private set; }
    public int    TgCount         { get; private set; }

    // ── FinOps ──────────────────────────────────────────────────────────────────
    public int    HaikuCount      { get; private set; }
    public int    SonnetCount     { get; private set; }
    public string TotalApiCostUsd { get; private set; } = "—";

    // ── Performance / eval (manually maintained in appsettings) ─────────────────
    public double? CategoryAccuracy   { get; private set; }
    public double? EmergencyRecall    { get; private set; }
    public double? EmergencyPrecision { get; private set; }
    public int     EvalTotalCases     { get; private set; }
    public string? EvalRunDate        { get; private set; }

    // ── AI category-confidence distribution (replaces the old fake average) ─────
    public int ConfHigh   { get; private set; }
    public int ConfMedium { get; private set; }
    public int ConfLow    { get; private set; }
    public int ConfTotal => ConfHigh + ConfMedium + ConfLow;
    public int ConfPct(int n) => ConfTotal > 0 ? (int)Math.Round((double)n / ConfTotal * 100) : 0;

    // ── Live feed ────────────────────────────────────────────────────────────────
    public IReadOnlyList<FeedItem> RecentTickets { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Demo realism (D2): exclude dev/test Mock-channel tickets everywhere.
        var tickets = _db.Tickets.Where(t => t.SourceMessage!.ChannelType != ChannelType.Mock);

        TotalTickets   = await tickets.CountAsync(ct);
        EmergencyCount = await tickets.CountAsync(t => t.IsEmergency, ct);
        SonnetCount    = await tickets.CountAsync(t => t.EscalatedToSonnet, ct);
        HaikuCount     = TotalTickets - SonnetCount;

        var channels = await tickets
            .GroupBy(t => t.SourceMessage!.ChannelType)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        WaCount = channels.FirstOrDefault(x => x.Key == ChannelType.WhatsApp)?.Count ?? 0;
        TgCount = channels.FirstOrDefault(x => x.Key == ChannelType.Telegram)?.Count ?? 0;

        ActiveResidents = await _db.Residents.CountAsync(r => r.IsActive, ct);
        TotalResidents  = await _db.Residents.CountAsync(ct);
        TotalMessages   = await _db.Messages.CountAsync(m => m.ChannelType != ChannelType.Mock, ct);

        var created = await tickets.Select(t => t.CreatedAt).ToListAsync(ct);
        var activeDays = created.Select(u => IstanbulTime.FromUtc(u).Date).Distinct().Count();
        DailyAvgTickets = activeDays > 0 ? Math.Round((double)TotalTickets / activeDays, 1) : 0;

        var conf = await tickets
            .GroupBy(t => t.CategoryConfidence)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        ConfHigh   = conf.FirstOrDefault(x => x.Key == ConfidenceLevel.High)?.Count   ?? 0;
        ConfMedium = conf.FirstOrDefault(x => x.Key == ConfidenceLevel.Medium)?.Count ?? 0;
        ConfLow    = conf.FirstOrDefault(x => x.Key == ConfidenceLevel.Low)?.Count    ?? 0;

        // Eval values — manually updated after each eval run.
        var eval = _config.GetSection("Dashboard:Eval");
        CategoryAccuracy   = ParseDouble(eval["CategoryAccuracy"]);
        EmergencyRecall    = ParseDouble(eval["EmergencyRecall"]);
        EmergencyPrecision = ParseDouble(eval["EmergencyPrecision"]);
        EvalTotalCases     = eval.GetValue<int?>("TotalCases") ?? 48;
        EvalRunDate        = eval["RunDate"];
        TotalApiCostUsd    = _config["Dashboard:TotalApiCostUsd"] ?? "—";

        var raw = await tickets
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        RecentTickets = raw.Select(t => new FeedItem(
            Id:          t.Id,
            Category:    t.Category,
            Severity:    t.Severity,
            IsEmergency: t.IsEmergency,
            Channel:     t.SourceMessage?.ChannelType ?? ChannelType.Mock,
            Preview:     TruncatePreview(t.SourceMessage?.RawText),
            Resident:    t.Resident?.DisplayName ?? t.Resident?.ApartmentNumber,
            TimeIst:     IstanbulTime.Format(t.CreatedAt)
        )).ToList();
    }

    private static double? ParseDouble(string? v)
        => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string TruncatePreview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "—";
        return text.Length > 60 ? text[..60] + "…" : text;
    }

    public sealed record FeedItem(
        Guid Id,
        TicketCategory Category,
        TicketSeverity Severity,
        bool IsEmergency,
        ChannelType Channel,
        string Preview,
        string? Resident,
        string TimeIst);
}
