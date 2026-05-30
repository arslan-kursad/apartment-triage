using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Web.Endpoints;

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/tickets/{id:guid}/status", UpdateStatus);
    }

    // PATCH /api/tickets/{id}/status — Body: { status: "Resolved" }
    // Status-field-only update; does NOT touch triage/pipeline logic.
    private static async Task<IResult> UpdateStatus(
        Guid id,
        TicketStatusRequest req,
        ApartmentTriageDbContext db,
        CancellationToken ct)
    {
        if (!Enum.TryParse<TicketStatus>(req.Status, ignoreCase: true, out var status))
            return Results.Ok(new { success = false, error = $"Geçersiz statü: {req.Status}" });

        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
            return Results.Ok(new { success = false, error = "Ticket bulunamadı." });

        ticket.UpdateStatus(status);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { success = true, status = status.ToString() });
    }

    public sealed record TicketStatusRequest(string Status);
}
