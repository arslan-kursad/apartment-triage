using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Infrastructure.Persistence;
using ApartmentTriage.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Web.Pages.Residents;

public sealed class IndexModel : PageModel
{
    private readonly ApartmentTriageDbContext _db;

    public IndexModel(ApartmentTriageDbContext db) => _db = db;

    // ── Filter bindings (Tickets filter pattern) ──────────────────────────────
    /// <summary>Free-text search over name + apartment/block.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>"whatsapp" | "telegram" | null (all).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Channel { get; set; }

    /// <summary>"active" (default) | "inactive" | "all".</summary>
    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "active";

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    // ── Pagination ────────────────────────────────────────────────────────────
    private static readonly int[] AllowedPageSizes = [10, 20, 50];

    [BindProperty(SupportsGet = true)] public int CurrentPage     { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 20;

    public IReadOnlyList<ResidentRow> Rows { get; private set; } = [];
    public ResidentRow?               EditRow { get; private set; }
    public int ActiveCount   { get; private set; }
    public int InactiveCount { get; private set; }

    public int TotalMatching { get; private set; }
    public int TotalPages => PageSize > 0 ? Math.Max(1, (int)Math.Ceiling((double)TotalMatching / PageSize)) : 1;
    public int FirstRow => TotalMatching == 0 ? 0 : (CurrentPage - 1) * PageSize + 1;
    public int LastRow  => Math.Min(CurrentPage * PageSize, TotalMatching);
    public IReadOnlyList<int> PageSizeOptions => AllowedPageSizes;

    /// <summary>Builds a /residents URL preserving the active filters (search/channel/status).</summary>
    public string BuildUrl(int page, int? pageSize = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(Q))       qs.Add($"Q={Uri.EscapeDataString(Q)}");
        if (!string.IsNullOrWhiteSpace(Channel)) qs.Add($"Channel={Uri.EscapeDataString(Channel)}");
        if (!string.IsNullOrWhiteSpace(Status))  qs.Add($"Status={Uri.EscapeDataString(Status)}");
        qs.Add($"CurrentPage={page}");
        qs.Add($"PageSize={pageSize ?? PageSize}");
        return "/residents?" + string.Join("&", qs);
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        ActiveCount   = await _db.Residents.CountAsync(r =>  r.IsActive, ct);
        InactiveCount = await _db.Residents.CountAsync(r => !r.IsActive, ct);

        var query = _db.Residents.AsQueryable();

        // Status: active (default) / inactive / all
        if (Status == "inactive")      query = query.Where(r => !r.IsActive);
        else if (Status != "all")      query = query.Where(r => r.IsActive);

        // Channel presence
        if (Channel == "whatsapp")     query = query.Where(r => r.WhatsAppNumber != null);
        else if (Channel == "telegram") query = query.Where(r => r.TelegramId != null);

        // Free-text search over all visible identity columns (Postgres ILike, accent/case-insensitive)
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = $"%{Q.Trim()}%";
            query = query.Where(r =>
                (r.DisplayName != null      && EF.Functions.ILike(r.DisplayName, term)) ||
                (r.ApartmentNumber != null  && EF.Functions.ILike(r.ApartmentNumber, term)) ||
                (r.WhatsAppNumber != null   && EF.Functions.ILike(r.WhatsAppNumber, term)) ||
                (r.ContactPhone != null     && EF.Functions.ILike(r.ContactPhone, term)) ||
                (r.TelegramUsername != null && EF.Functions.ILike(r.TelegramUsername, term)));
        }

        // Pagination — validate page size, count matches, clamp page into range
        if (!AllowedPageSizes.Contains(PageSize)) PageSize = 20;
        TotalMatching = await query.CountAsync(ct);
        if (CurrentPage < 1) CurrentPage = 1;
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;

        var residents = await query
            .OrderBy(r => r.ApartmentNumber)
            .ThenBy(r => r.DisplayName)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var residentIds = residents.Select(r => r.Id).ToList();

        var lastActivities = await _db.Messages
            .Where(m => residentIds.Contains(m.ResidentId))
            .GroupBy(m => m.ResidentId)
            .Select(g => new { ResidentId = g.Key, LastAt = g.Max(m => m.ReceivedAt) })
            .ToDictionaryAsync(x => x.ResidentId, x => x.LastAt, ct);

        Rows = residents.Select(r => new ResidentRow(
            Id:              r.Id,
            Initials:        BuildInitials(r),
            Name:            r.DisplayName ?? "—",
            ApartmentNumber: r.ApartmentNumber ?? "—",
            HasWhatsApp:     r.WhatsAppNumber is not null,
            HasTelegram:     r.TelegramId.HasValue,
            PhoneDisplay:    BuildPhoneDisplay(r),
            WhatsAppNumber:  r.WhatsAppNumber,
            ContactPhone:    r.ContactPhone,
            TelegramUsername: r.TelegramUsername,
            LastActivity:    lastActivities.TryGetValue(r.Id, out var lat)
                                 ? IstanbulTime.Format(lat)
                                 : "—",
            IsActive:        r.IsActive
        )).ToList();

        // ?edit={id} — auto-open modal on page load (may be inactive, load separately)
        if (Edit.HasValue)
        {
            EditRow = Rows.FirstOrDefault(r => r.Id == Edit.Value);
            if (EditRow is null)
            {
                var r = await _db.Residents.FirstOrDefaultAsync(x => x.Id == Edit.Value, ct);
                if (r is not null)
                    EditRow = new ResidentRow(r.Id, BuildInitials(r), r.DisplayName ?? "—",
                        r.ApartmentNumber ?? "—", r.WhatsAppNumber is not null, r.TelegramId.HasValue,
                        BuildPhoneDisplay(r), r.WhatsAppNumber, r.ContactPhone, r.TelegramUsername,
                        "—", r.IsActive);
            }
        }
    }

    private static string BuildInitials(Resident r)
    {
        var apt = r.ApartmentNumber;
        if (!string.IsNullOrEmpty(apt))
        {
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

    private static string? BuildPhoneDisplay(Resident r)
    {
        if (r.WhatsAppNumber is { Length: > 4 } wa)
            return wa[..4] + "***" + wa[^4..];
        if (r.WhatsAppNumber is { Length: > 0 } waShort)
            return waShort;
        if (r.TelegramId.HasValue)
            return $"TG #{r.TelegramId}";
        return null;
    }

    public sealed record ResidentRow(
        Guid    Id,
        string  Initials,
        string  Name,
        string  ApartmentNumber,
        bool    HasWhatsApp,
        bool    HasTelegram,
        string? PhoneDisplay,
        string? WhatsAppNumber,
        string? ContactPhone,
        string? TelegramUsername,
        string  LastActivity,
        bool    IsActive);
}
