using ApartmentTriage.Application.Channels;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApartmentTriage.Web.Endpoints;

public static class ReplyEndpoints
{
    public static void MapReplyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/messages/{ticketId:guid}/reply", SendReply);
    }

    private static async Task<IResult> SendReply(
        Guid ticketId,
        ReplyRequest body,
        ApartmentTriageDbContext db,
        IServiceProvider services,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Text))
            return Results.Ok(new { success = false, error = "Reply text is empty." });

        var ticket = await db.Tickets
            .Include(t => t.SourceMessage)
            .Include(t => t.Resident)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

        if (ticket is null)
            return Results.Ok(new { success = false, error = "Ticket not found." });

        var channelType = ticket.SourceMessage?.ChannelType;
        if (channelType is null)
            return Results.Ok(new { success = false, error = "Channel type unknown." });

        var recipientId = BuildRecipientId(channelType.Value, ticket.Resident);
        if (recipientId is null)
            return Results.Ok(new
            {
                success = false,
                error = $"No {channelType} contact info for resident."
            });

        IMessageChannel? channel = null;
        try
        {
            channel = services.GetKeyedService<IMessageChannel>(channelType.Value);
        }
        catch
        {
            // GetKeyedService returns null if not registered; swallow any exception
        }

        if (channel is null)
            return Results.Ok(new
            {
                success = false,
                error = $"Channel {channelType} not configured."
            });

        try
        {
            await channel.SendAsync(recipientId, body.Text, ct);
            return Results.Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                success = false,
                error = $"Send failed: {ex.Message}"
            });
        }
    }

    private static string? BuildRecipientId(ChannelType channelType, Domain.Entities.Resident? resident)
    {
        if (resident is null) return null;
        return channelType switch
        {
            ChannelType.Telegram  => resident.TelegramId?.ToString(),
            ChannelType.WhatsApp  => resident.WhatsAppNumber,
            _                     => null
        };
    }

    private sealed record ReplyRequest(string Text);
}
