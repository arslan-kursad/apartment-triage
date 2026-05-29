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

    [BindProperty(SupportsGet = true)]
    public bool ShowInactive { get; set; } = false;

    public IReadOnlyList<ResidentRow> Rows { get; private set; } = [];
    public int ActiveCount   { get; private set; }
    public int InactiveCount { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        ActiveCount   = await _db.Residents.CountAsync(r =>  r.IsActive, ct);
        InactiveCount = await _db.Residents.CountAsync(r => !r.IsActive, ct);

        var query = _db.Residents.AsQueryable();
        if (!ShowInactive)
            query = query.Where(r => r.IsActive);

        var residents = await query
            .OrderBy(r => r.ApartmentNumber)
            .ThenBy(r => r.DisplayName)
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
            LastActivity:    lastActivities.TryGetValue(r.Id, out var lat)
                                 ? IstanbulTime.Format(lat)
                                 : "—",
            IsActive:        r.IsActive
        )).ToList();
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
        string  LastActivity,
        bool    IsActive);
}
