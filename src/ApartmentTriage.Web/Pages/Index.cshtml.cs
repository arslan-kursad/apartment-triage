using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Web.Helpers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly ApartmentTriageDbContext _db;

    public IndexModel(ApartmentTriageDbContext db) => _db = db;

    // KPI
    public int TotalTickets     { get; private set; }
    public int EmergencyCount   { get; private set; }
    public double AvgConfidence { get; private set; }
    public int WaCount          { get; private set; }
    public int TgCount          { get; private set; }

    // Live feed
    public IReadOnlyList<FeedItem> RecentTickets { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        TotalTickets   = await _db.Tickets.CountAsync(ct);
        EmergencyCount = await _db.Tickets.CountAsync(t => t.IsEmergency, ct);

        var confidences = await _db.Tickets
            .Select(t => (int)t.CategoryConfidence)
            .ToListAsync(ct);
        AvgConfidence = confidences.Count > 0
            ? Math.Round(confidences.Average() / 2.0 * 100.0, 1)
            : 0;

        var channels = await _db.Tickets
            .Join(_db.Messages, t => t.SourceMessageId, m => m.Id, (_, m) => m.ChannelType)
            .GroupBy(ct2 => ct2)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        WaCount = channels.FirstOrDefault(x => x.Channel == ChannelType.WhatsApp)?.Count ?? 0;
        TgCount = channels.FirstOrDefault(x => x.Channel == ChannelType.Telegram)?.Count ?? 0;

        var raw = await _db.Tickets
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        RecentTickets = raw.Select(t => new FeedItem(
            Id:        t.Id,
            Category:  t.Category,
            Severity:  t.Severity,
            IsEmergency: t.IsEmergency,
            Channel:   t.SourceMessage?.ChannelType ?? ChannelType.Mock,
            Preview:   TruncatePreview(t.SourceMessage?.RawText),
            Resident:  t.Resident?.ApartmentNumber ?? "—",
            TimeIst:   IstanbulTime.Format(t.CreatedAt)
        )).ToList();
    }

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
        string Resident,
        string TimeIst);
}
