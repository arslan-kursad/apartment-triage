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

    [BindProperty(SupportsGet = true)] public Guid?   TicketId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q        { get; set; }
    [BindProperty(SupportsGet = true)] public string? Range    { get; set; }
    [BindProperty(SupportsGet = true)] public int     CurrentPage { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int     PageSize    { get; set; } = 20;

    private static readonly int[] AllowedPageSizes = [10, 20, 50];

    public IReadOnlyList<InboxItem> Items  { get; private set; } = [];
    public TicketDetail?            Detail { get; private set; }

    public int TotalMatching { get; private set; }
    public int TotalPages => PageSize > 0 ? Math.Max(1, (int)Math.Ceiling((double)TotalMatching / PageSize)) : 1;
    public int FirstRow => TotalMatching == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;
    public int LastRow  => Math.Min(CurrentPage * PageSize, TotalMatching);
    public IReadOnlyList<int> PageSizeOptions => AllowedPageSizes;
    public string EffectiveRange => DateFilter.Normalize(Range);

    // ── URL builders — preserve filter/selection state across navigation ───────
    private string Compose(Guid? ticketId, int page, int size, string? range)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(Q)) qs.Add($"Q={Uri.EscapeDataString(Q)}");
        var r = DateFilter.Normalize(range);
        if (r != DateFilter.All) qs.Add($"Range={r}");
        if (ticketId.HasValue) qs.Add($"TicketId={ticketId}");
        qs.Add($"CurrentPage={page}");
        qs.Add($"PageSize={size}");
        return "/inbox?" + string.Join("&", qs);
    }

    public string SelectUrl(Guid ticketId) => Compose(ticketId, CurrentPage, PageSize, EffectiveRange);
    public string PageUrl(int page)         => Compose(TicketId, page, PageSize, EffectiveRange);
    public string PageSizeUrl(int size)     => Compose(TicketId, 1, size, EffectiveRange);
    public string PresetUrl(string range)   => Compose(null, 1, PageSize, range);

    public async Task OnGetAsync(CancellationToken ct)
    {
        // Left panel: filtered + paginated tickets with source message + resident
        var query = _db.Tickets
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .AsQueryable();

        // Search by resident display name (registered residents)
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = $"%{Q.Trim()}%";
            query = query.Where(t => t.Resident != null && t.Resident.DisplayName != null
                                     && EF.Functions.ILike(t.Resident.DisplayName, term));
        }

        // Date preset filter on source-message arrival time
        var (fromUtc, toUtc) = DateFilter.Resolve(Range);
        if (fromUtc.HasValue) query = query.Where(t => t.SourceMessage!.ReceivedAt >= fromUtc.Value);
        if (toUtc.HasValue)   query = query.Where(t => t.SourceMessage!.ReceivedAt <  toUtc.Value);

        // Pagination — validate size, count, clamp page
        if (!AllowedPageSizes.Contains(PageSize)) PageSize = 20;
        TotalMatching = await query.CountAsync(ct);
        if (CurrentPage < 1) CurrentPage = 1;
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
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
