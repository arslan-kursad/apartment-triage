using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApartmentTriage.Web.Pages.Tickets;

public sealed class IndexModel : PageModel
{
    private const int PageSize = 20;

    private readonly ITicketRepository _tickets;

    public IndexModel(ITicketRepository tickets) => _tickets = tickets;

    // ── Filter bindings ───────────────────────────────────────────────────────

    [BindProperty(SupportsGet = true)] public TicketStatus?   FilterStatus      { get; set; }
    [BindProperty(SupportsGet = true)] public TicketCategory? FilterCategory    { get; set; }
    [BindProperty(SupportsGet = true)] public bool?           FilterIsEmergency { get; set; }
    [BindProperty(SupportsGet = true)] public string?         Range             { get; set; }
    [BindProperty(SupportsGet = true)] public int             CurrentPage       { get; set; } = 1;

    // ── Cross-link query params (overview / residents deep-links) ──────────────
    [BindProperty(SupportsGet = true, Name = "emergency")] public bool? Emergency { get; set; }
    [BindProperty(SupportsGet = true, Name = "resident")]  public Guid? Resident  { get; set; }

    /// <summary>Effective emergency filter — dropdown takes precedence over the deep-link param.</summary>
    public bool? EffectiveEmergency => FilterIsEmergency ?? Emergency;

    // ── Page result ───────────────────────────────────────────────────────────

    public IReadOnlyList<Ticket> Tickets    { get; private set; } = [];
    public int                   TotalCount { get; private set; }
    public int                   TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>Display label for the active resident filter chip (apartment or "—").</summary>
    public string? ResidentLabel { get; private set; }

    // Enum values for filter dropdowns
    public IEnumerable<TicketStatus>   AllStatuses    => Enum.GetValues<TicketStatus>();
    public IEnumerable<TicketCategory> AllCategories  => Enum.GetValues<TicketCategory>();

    public string EffectiveRange => DateFilter.Normalize(Range);

    /// <summary>Builds a /tickets URL preserving every active filter; optionally overrides the date range.</summary>
    public string BuildUrl(int page, string? range = null)
    {
        var qs = new List<string>();
        if (FilterStatus.HasValue)      qs.Add($"FilterStatus={FilterStatus}");
        if (FilterCategory.HasValue)    qs.Add($"FilterCategory={FilterCategory}");
        if (FilterIsEmergency.HasValue) qs.Add($"FilterIsEmergency={(FilterIsEmergency.Value ? "true" : "false")}");
        if (Emergency.HasValue)         qs.Add($"emergency={(Emergency.Value ? "true" : "false")}");
        if (Resident.HasValue)          qs.Add($"resident={Resident}");
        var r = DateFilter.Normalize(range ?? Range);
        if (r != DateFilter.All)        qs.Add($"Range={r}");
        qs.Add($"CurrentPage={page}");
        return "/tickets?" + string.Join("&", qs);
    }

    public string PresetUrl(string range) => BuildUrl(1, range);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (CurrentPage < 1) CurrentPage = 1;

        var (fromUtc, toUtc) = DateFilter.Resolve(Range);

        var (items, total) = await _tickets.GetPagedAsync(
            FilterStatus,
            FilterCategory,
            EffectiveEmergency,
            Resident,
            CurrentPage,
            PageSize,
            fromUtc,
            toUtc,
            cancellationToken);

        Tickets    = items;
        TotalCount = total;

        // Resident chip label — derive from the loaded page to avoid an extra lookup.
        if (Resident.HasValue)
            ResidentLabel = items.FirstOrDefault()?.Resident?.ApartmentNumber
                            ?? items.FirstOrDefault()?.Resident?.DisplayName;
    }
}
