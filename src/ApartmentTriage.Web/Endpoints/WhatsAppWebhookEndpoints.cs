using System.Text.Json;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Channels;
using Microsoft.AspNetCore.Mvc;

namespace ApartmentTriage.Web.Endpoints;

public static class WhatsAppWebhookEndpoints
{
    // S&C constraint: path is fixed — no path parameters.
    private const string WebhookPath = "/api/webhook/whatsapp";

    // Marker for ILogger<T> — static classes cannot be type arguments.
    private sealed class Log { }

    public static IEndpointRouteBuilder MapWhatsAppWebhook(this IEndpointRouteBuilder app)
    {
        // GET: Meta webhook verification handshake
        app.MapGet(WebhookPath, VerifyWebhook);

        // POST: incoming messages from WhatsApp Cloud API
        app.MapPost(WebhookPath, ReceiveMessage);

        return app;
    }

    // GET /api/webhook/whatsapp?hub.mode=subscribe&hub.verify_token=...&hub.challenge=...
    private static IResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IConfiguration configuration,
        ILogger<Log> logger)
    {
        var expectedToken = configuration["WhatsApp:WebhookVerifyToken"];

        if (mode != "subscribe" ||
            verifyToken != expectedToken ||
            string.IsNullOrEmpty(challenge))
        {
            logger.LogWarning("WhatsApp webhook verification failed — mode={Mode} tokenMatch={Match}",
                mode, verifyToken == expectedToken);
            return Results.Forbid();
        }

        logger.LogInformation("WhatsApp webhook verified successfully");
        // Meta expects plain text — not JSON. Results.Ok(int) would serialize as JSON.
        return Results.Content(challenge, "text/plain");
    }

    // POST /api/webhook/whatsapp
    private static async Task<IResult> ReceiveMessage(
        HttpRequest request,
        [FromKeyedServices(ChannelType.WhatsApp)] WhatsAppAdapter adapter,
        IConfiguration configuration,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        // Read raw body for signature verification
        request.EnableBuffering();
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        var body = ms.ToArray();
        request.Body.Position = 0;

        // Signature verification (Day 14 S&C blocker — enforced now)
        var appSecret = configuration["WhatsApp:AppSecret"];

        if (!string.IsNullOrEmpty(appSecret))
        {
            var signature = request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!WhatsAppAdapter.VerifySignature(appSecret, body, signature))
            {
                logger.LogWarning("WhatsApp webhook: invalid signature — request rejected");
                return Results.Forbid();
            }
        }
        else
        {
            logger.LogWarning("WhatsApp:AppSecret not configured — signature verification skipped");
        }

        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "WhatsApp webhook: malformed JSON body");
            return Results.BadRequest();
        }

        var enqueued = 0;
        foreach (var incoming in WhatsAppAdapter.ParseWebhookPayload(root))
        {
            if (adapter.TryEnqueue(incoming))
                enqueued++;
        }

        if (enqueued > 0)
            logger.LogInformation("WhatsApp webhook: enqueued {Count} message(s)", enqueued);

        // WhatsApp requires 200 OK — any other status triggers retry storm
        return Results.Ok();
    }
}
