using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ApartmentTriage.Web.Endpoints;

public static class ResidentEndpoints
{
    private static readonly Regex E164Regex = new(@"^\+\d{10,15}$", RegexOptions.Compiled);
    // Turkish address content (BLOK / DAİRE / NUMARA) → uppercase with TR casing
    // so "daire" → "DAİRE" (dotted İ), matching existing seed data.
    private static readonly CultureInfo TrCulture = new("tr-TR");

    public static void MapResidentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/residents",                          GetResidents);
        app.MapPost("/api/residents",                         CreateResident);
        app.MapPut("/api/residents/{id:guid}",               UpdateResident);
        app.MapPatch("/api/residents/{id:guid}/status",      UpdateStatus);
    }

    // GET /api/residents?showInactive=false
    private static async Task<IResult> GetResidents(
        ApartmentTriageDbContext db,
        bool showInactive = false,
        CancellationToken ct = default)
    {
        var query = db.Residents.AsQueryable();
        if (!showInactive) query = query.Where(r => r.IsActive);

        var residents = await query
            .OrderBy(r => r.ApartmentNumber)
            .ThenBy(r => r.DisplayName)
            .ToListAsync(ct);

        var ids = residents.Select(r => r.Id).ToList();
        var lastActivities = await db.Messages
            .Where(m => ids.Contains(m.ResidentId))
            .GroupBy(m => m.ResidentId)
            .Select(g => new { ResidentId = g.Key, LastAt = g.Max(m => m.ReceivedAt) })
            .ToDictionaryAsync(x => x.ResidentId, x => (DateTime?)x.LastAt, ct);

        var result = residents.Select(r => new
        {
            id               = r.Id,
            displayName      = r.DisplayName,
            apartmentNumber  = r.ApartmentNumber,
            whatsAppNumber   = r.WhatsAppNumber,
            telegramId       = r.TelegramId,
            contactPhone     = r.ContactPhone,
            preferredLanguage = r.PreferredLanguage,
            isActive         = r.IsActive,
            lastActivityAt   = lastActivities.TryGetValue(r.Id, out var lat) ? lat : (DateTime?)null,
            channelFlags     = new { whatsApp = r.WhatsAppNumber is not null, telegram = r.TelegramId.HasValue }
        });

        return Results.Ok(result);
    }

    // POST /api/residents → 201 Created
    private static async Task<IResult> CreateResident(
        ResidentUpsertRequest req,
        ApartmentTriageDbContext db,
        CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return Results.Ok(new { success = false, error = err });

        var resident = Resident.Create(
            displayName:      req.DisplayName?.Trim(),
            apartmentNumber:  NormalizeUnit(req.ApartmentNumber),
            whatsAppNumber:   NormalizePhone(req.WhatsAppNumber),
            telegramId:       req.TelegramId,
            preferredLanguage: req.PreferredLanguage is "tr" or "en" ? req.PreferredLanguage : "tr");

        resident.UpdateContactInfo(
            contactPhone:     NormalizePhone(req.ContactPhone));

        await db.Residents.AddAsync(resident, ct);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/residents/{resident.Id}", new { success = true, id = resident.Id });
    }

    // PUT /api/residents/{id}
    private static async Task<IResult> UpdateResident(
        Guid id,
        ResidentUpsertRequest req,
        ApartmentTriageDbContext db,
        CancellationToken ct)
    {
        var err = Validate(req);
        if (err is not null) return Results.Ok(new { success = false, error = err });

        var resident = await db.Residents.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resident is null)
            return Results.Ok(new { success = false, error = "Sakin bulunamadı." });

        // FLAG 3 guard: never overwrite WhatsApp with a masked value ("+90***1234").
        // Masked strings originate from the list-view display; persisting one would
        // corrupt the channel identity. Pass null → UpdateContactInfo keeps existing.
        var whatsApp = NormalizePhone(req.WhatsAppNumber);
        if (whatsApp is not null && whatsApp.Contains('*'))
            whatsApp = null;

        resident.UpdateContactInfo(
            displayName:      req.DisplayName?.Trim(),
            apartmentNumber:  NormalizeUnit(req.ApartmentNumber),
            whatsAppNumber:   whatsApp,
            telegramId:       req.TelegramId,
            contactPhone:     NormalizePhone(req.ContactPhone));

        if (req.PreferredLanguage is "tr" or "en")
            resident.SetPreferredLanguage(req.PreferredLanguage);

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    // PATCH /api/residents/{id}/status
    private static async Task<IResult> UpdateStatus(
        Guid id,
        StatusRequest req,
        ApartmentTriageDbContext db,
        CancellationToken ct)
    {
        var resident = await db.Residents.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (resident is null)
            return Results.Ok(new { success = false, error = "Sakin bulunamadı." });

        if (req.IsActive) resident.Reactivate();
        else              resident.Deactivate();

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static string? Validate(ResidentUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName) || req.DisplayName.Trim().Length < 2)
            return "Ad Soyad en az 2 karakter olmalıdır.";
        // Validate the normalized number — input may carry spaces/dashes the user typed.
        var wa = NormalizePhone(req.WhatsAppNumber);
        if (wa is not null && !wa.Contains('*') && !E164Regex.IsMatch(wa))
            return "WhatsApp numarası E.164 formatında olmalıdır (+905xxxxxxxxx).";
        var contact = NormalizePhone(req.ContactPhone);
        if (contact is not null && !contact.Contains('*') && !E164Regex.IsMatch(contact))
            return "İletişim telefonu E.164 formatında olmalıdır (+905xxxxxxxxx).";
        return null;
    }

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Normalization: enforce storage consistency regardless of how it was typed ──

    /// <summary>Unit/block → trimmed, collapsed whitespace, Turkish uppercase.</summary>
    private static string? NormalizeUnit(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var collapsed = Regex.Replace(s.Trim(), @"\s+", " ");
        return collapsed.ToUpper(TrCulture);
    }

    /// <summary>Phone/WhatsApp → strip spaces, dashes, dots, parentheses (E.164 digits only).</summary>
    private static string? NormalizePhone(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cleaned = Regex.Replace(s.Trim(), @"[\s\-().]", "");
        return cleaned.Length == 0 ? null : cleaned;
    }
}

public sealed record ResidentUpsertRequest(
    string? DisplayName,
    string? ApartmentNumber      = null,
    string? WhatsAppNumber       = null,
    long?   TelegramId           = null,
    string? ContactPhone         = null,
    string  PreferredLanguage    = "tr");

public sealed record StatusRequest(bool IsActive);
