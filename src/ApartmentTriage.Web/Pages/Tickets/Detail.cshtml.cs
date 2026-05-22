using System.Text.Json;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApartmentTriage.Web.Pages.Tickets;

public sealed class DetailModel : PageModel
{
    private readonly ITicketRepository _tickets;

    public DetailModel(ITicketRepository tickets) => _tickets = tickets;

    public Ticket? Ticket { get; private set; }

    /// <summary>Deserialized AmbiguityReasons from Ticket.AmbiguityReasonsJson.</summary>
    public IReadOnlyList<AmbiguityReason> AmbiguityReasons { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Ticket = await _tickets.GetByIdAsync(id, cancellationToken);

        if (Ticket is null)
            return NotFound();

        if (Ticket.AmbiguityReasonsJson is not null)
        {
            try
            {
                AmbiguityReasons = JsonSerializer.Deserialize<List<AmbiguityReason>>(
                    Ticket.AmbiguityReasonsJson,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
                    }) ?? [];
            }
            catch (JsonException)
            {
                // Defensive: malformed JSON — render empty list
                AmbiguityReasons = [];
            }
        }

        return Page();
    }
}
