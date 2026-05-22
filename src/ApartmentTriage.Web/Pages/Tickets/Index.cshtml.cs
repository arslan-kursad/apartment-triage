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

    // ── Page result ───────────────────────────────────────────────────────────

    public IReadOnlyList<Ticket> Tickets    { get; private set; } = [];
    public int                   TotalCount { get; private set; }
    public int                   TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    // Enum values for filter dropdowns
    public IEnumerable<TicketStatus>   AllStatuses    => Enum.GetValues<TicketStatus>();
    public IEnumerable<TicketCategory> AllCategories  => Enum.GetValues<TicketCategory>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (CurrentPage < 1) CurrentPage = 1;

        var (items, total) = await _tickets.GetPagedAsync(
            FilterStatus,
            FilterCategory,
            FilterIsEmergency,
            CurrentPage,
            PageSize,
            cancellationToken);

        Tickets    = items;
        TotalCount = total;
    }
}
