using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Web.Pages.Inbox;

public sealed class IndexModel : PageModel
{
    private readonly ApartmentTriageDbContext _db;

    public IndexModel(ApartmentTriageDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)]
    public Guid? TicketId { get; set; }

    public IReadOnlyList<InboxItem> Items  { get; private set; } = [];
    public TicketDetail?            Detail { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Left panel: last 30 tickets with source message + resident
        var tickets = await _db.Tickets
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .OrderByDescending(t => t.CreatedAt)
            .Take(30)
            .ToListAsync(ct);

        Items = tickets.Select(t =>
        {
            var ch = t.SourceMessage?.ChannelType ?? ChannelType.Mock;
            return new InboxItem(
                TicketId:    t.Id,
                Resident:    ResidentLabel(t.Resident),
                Initials:    Initials(t.Resident),
                Preview:     TruncatePreview(t.SourceMessage?.RawText),
                Channel:     ch,
                IsEmergency: t.IsEmergency,
                TimeIst:     IstanbulTime.Format(t.CreatedAt));
        }).ToList();

        // Determine selected ticket
        var selectedId = TicketId ?? tickets.FirstOrDefault()?.Id;
        if (selectedId.HasValue)
        {
            var sel = tickets.FirstOrDefault(t => t.Id == selectedId.Value)
                ?? await _db.Tickets
                    .Include(t => t.SourceMessage)
                    .Include(t => t.Resident)
                    .FirstOrDefaultAsync(t => t.Id == selectedId.Value, ct);

            if (sel is not null)
            {
                var lang = Request.Cookies["atriage_lang"] ?? "tr";
                var draftReply = ReplyTemplates.BuildTicketReply(sel, lang);

                var selChannel = sel.SourceMessage?.ChannelType ?? ChannelType.Mock;
                Detail = new TicketDetail(
                    TicketId:      sel.Id,
                    Resident:      ResidentLabel(sel.Resident),
                    ResidentDisplayName: sel.Resident?.DisplayName,
                    Initials:      Initials(sel.Resident),
                    Channel:       selChannel,
                    RawText:       sel.SourceMessage?.RawText ?? "—",
                    ReceivedAt:    IstanbulTime.Format(sel.SourceMessage?.ReceivedAt ?? sel.CreatedAt),
                    Category:      sel.Category,
                    Severity:      sel.Severity,
                    IsEmergency:   sel.IsEmergency,
                    Confidence:    sel.CategoryConfidence,
                    LocationHint:  sel.LocationHint,
                    RoutingAction: sel.RoutingAction,
                    DraftReply:               draftReply,
                    Status:                   sel.Status,
                    ResidentId:               sel.Resident?.Id,
                    ResidentApartmentNumber:  sel.Resident?.ApartmentNumber
                );
            }
        }
    }

    // Name only — never the channel. Channel is rendered separately as an icon.
    // Returns null when no name; the view renders an i18n "Sakin/Resident" fallback.
    private static string? ResidentLabel(Resident? r)
        => r?.DisplayName is { Length: > 0 } name ? name : null;

    private static string Initials(Resident? r)
    {
        if (r is null) return "?";

        var apt = r.ApartmentNumber;
        if (!string.IsNullOrEmpty(apt))
        {
            // "Daire 7" → "D7", "Daire 12" → "D12"
            if (apt.StartsWith("Daire ", StringComparison.OrdinalIgnoreCase))
                return "D" + apt["Daire ".Length..].Trim();
            return apt.Length >= 2 ? apt[..2].ToUpperInvariant() : apt.ToUpperInvariant();
        }

        var name = r.DisplayName;
        if (string.IsNullOrEmpty(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
            : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }

    private static string TruncatePreview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "—";
        return text.Length > 50 ? text[..50] + "…" : text;
    }

    public sealed record InboxItem(
        Guid TicketId,
        string? Resident,
        string Initials,
        string Preview,
        ChannelType Channel,
        bool IsEmergency,
        string TimeIst);

    public sealed record TicketDetail(
        Guid TicketId,
        string? Resident,
        string? ResidentDisplayName,
        string Initials,
        ChannelType Channel,
        string RawText,
        string ReceivedAt,
        TicketCategory Category,
        TicketSeverity Severity,
        bool IsEmergency,
        ConfidenceLevel Confidence,
        string? LocationHint,
        RoutingAction? RoutingAction,
        string DraftReply,
        TicketStatus Status,
        Guid? ResidentId,
        string? ResidentApartmentNumber);
}
