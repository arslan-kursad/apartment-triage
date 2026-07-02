using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApartmentTriage.Web.Jobs;
using Hangfire;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace ApartmentTriage.Web.Endpoints;

public static class TelegramWebhookEndpoints
{
    // S&C constraint: path is fixed — no path parameters (mirrors WhatsApp webhook).
    private const string WebhookPath = "/api/webhook/telegram";

    // Telegram sends this header on every webhook request when SetWebhook was called with a secret_token.
    private const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    // Marker for ILogger<T> — static classes cannot be type arguments.
    private sealed class Log { }

    public static IEndpointRouteBuilder MapTelegramWebhook(this IEndpointRouteBuilder app)
    {
        // POST only: Telegram has no GET verification handshake (unlike Meta/WhatsApp).
        app.MapPost(WebhookPath, ReceiveUpdate);
        return app;
    }

    // POST /api/webhook/telegram
    private static async Task<IResult> ReceiveUpdate(
        HttpRequest request,
        IBackgroundJobClient backgroundJobs,
        IConfiguration configuration,
        ILogger<Log> logger,
        CancellationToken ct)
    {
        // Secret-token verification: Telegram echoes the secret set via SetWebhook in this header.
        var expectedSecret = configuration["TelegramBot:WebhookSecret"];

        if (!string.IsNullOrEmpty(expectedSecret))
        {
            var provided = request.Headers[SecretHeader].FirstOrDefault();
            if (!FixedTimeEquals(expectedSecret, provided))
            {
                logger.LogWarning("Telegram webhook: invalid secret token — request rejected");
                return Results.StatusCode(403);
            }
        }
        else
        {
            logger.LogWarning("TelegramBot:WebhookSecret not configured — secret verification skipped");
        }

        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        var body = ms.ToArray();

        Update? update;
        try
        {
            update = JsonSerializer.Deserialize<Update>(body, JsonBotAPI.Options);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Telegram webhook: malformed JSON body");
            return Results.BadRequest();
        }

        if (update is null)
        {
            logger.LogWarning("Telegram webhook: null update after deserialization");
            return Results.BadRequest();
        }

        var updateJson = Encoding.UTF8.GetString(body);
        var jobId = backgroundJobs.Enqueue<ChannelConsumerJob>(
            job => job.ProcessUpdateAsync(updateJson, CancellationToken.None));
        logger.LogInformation("Telegram webhook: enqueued update {UpdateId} as job {JobId}", update.Id, jobId);

        // Telegram retries on any non-2XX — always 200 so a transient triage hiccup
        // doesn't trigger redelivery storms (drain-side dedup handles duplicates).
        return Results.Ok();
    }

    /// <summary>Constant-time comparison of the configured secret against the request header.</summary>
    private static bool FixedTimeEquals(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided));
    }
}
