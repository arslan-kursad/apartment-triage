using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
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

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (CurrentPage < 1) CurrentPage = 1;

        var (items, total) = await _tickets.GetPagedAsync(
            FilterStatus,
            FilterCategory,
            EffectiveEmergency,
            Resident,
            CurrentPage,
            PageSize,
            cancellationToken);

        Tickets    = items;
        TotalCount = total;

        // Resident chip label — derive from the loaded page to avoid an extra lookup.
        if (Resident.HasValue)
            ResidentLabel = items.FirstOrDefault()?.Resident?.ApartmentNumber
                            ?? items.FirstOrDefault()?.Resident?.DisplayName;
    }
}
