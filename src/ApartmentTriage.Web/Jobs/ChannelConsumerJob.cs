using System.Text.Json;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentTriage.Web.Jobs;

public sealed class ChannelConsumerJob(
    [FromKeyedServices(ChannelType.Telegram)] IMessageChannel channel,
    IResidentRepository residentRepository,
    IMessageRepository messageRepository,
    ITicketRepository ticketRepository,
    ITriageOrchestrator orchestrator,
    ILogger<ChannelConsumerJob> logger)
{
    // 55s budget caps each polling iteration; BackgroundService sleeps 10s between runs.
    private static readonly TimeSpan JobBudget = TimeSpan.FromSeconds(55);

    public async Task RunAsync(CancellationToken hangfireCt = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(hangfireCt);
        cts.CancelAfter(JobBudget);

        try
        {
            await foreach (var incoming in channel.ReadMessagesAsync(cts.Token))
                await ProcessIncomingAsync(incoming, cts.Token);
        }
        catch (OperationCanceledException) when (!hangfireCt.IsCancellationRequested)
        {
            // Normal: 55s budget exhausted. Next execution starts in ~5s.
        }
    }

    private async Task ProcessIncomingAsync(IncomingMessage incoming, CancellationToken ct)
    {
        logger.LogInformation(
            "Received Telegram message {ExternalId} from {SenderId} ({TextLength} chars)",
            incoming.ExternalId, incoming.SenderId, incoming.Text.Length);

        if (await messageRepository.ExistsAsync(incoming.ExternalId, channel.ChannelType, ct))
        {
            logger.LogDebug("Skipping duplicate message {ExternalId}", incoming.ExternalId);
            return;
        }

        if (!long.TryParse(incoming.SenderId, out var telegramId))
        {
            logger.LogWarning("Non-numeric Telegram SenderId {SenderId} — skipping", incoming.SenderId);
            return;
        }

        var preferredLanguage = DetectLanguage(incoming.LanguageCode, incoming.Text);
        var resident = await residentRepository.FindByTelegramIdAsync(telegramId, ct)
            ?? await CreateResidentAsync(telegramId, preferredLanguage, ct);

        if (resident.PendingClarificationTicketId.HasValue)
        {
            await HandleClarificationResponseAsync(incoming, resident, ct);
            return;
        }

        var message = Message.Create(
            residentId: resident.Id,
            channelType: channel.ChannelType,
            externalMessageId: incoming.ExternalId,
            rawText: incoming.Text,
            receivedAt: incoming.ReceivedAt.UtcDateTime);

        await messageRepository.AddAsync(message, ct);
        await messageRepository.SaveChangesAsync(ct);

        var result = await orchestrator.ProcessAsync(
            message, resident.PreferredLanguage, incoming.ImageData, incoming.ImageMimeType, ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Triage failed for {ExternalId}: {Error}",
                incoming.ExternalId, result.Error!.Message);
            return;
        }

        message.MarkProcessed();
        await messageRepository.SaveChangesAsync(ct);

        if (result.ReplyText is not null)
        {
            logger.LogInformation(
                "Sending reply to {SenderId} (lang={Language})",
                incoming.SenderId, resident.PreferredLanguage);

            await channel.SendAsync(incoming.SenderId, result.ReplyText, ct);

            if (result.AmbiguityReasons.Count > 0 && result.Tickets.Count > 0)
            {
                resident.SetPendingClarification(result.Tickets[0].Id);
                await residentRepository.SaveChangesAsync(ct);
                logger.LogInformation(
                    "PendingClarificationTicketId={TicketId} set for resident {ResidentId}",
                    result.Tickets[0].Id, resident.Id);
            }
        }
    }

    private async Task HandleClarificationResponseAsync(
        IncomingMessage incoming, Resident resident, CancellationToken ct)
    {
        var pendingTicketId = resident.PendingClarificationTicketId!.Value;

        // Save the clarification response as a message for idempotency across restarts.
        var message = Message.Create(
            residentId: resident.Id,
            channelType: channel.ChannelType,
            externalMessageId: incoming.ExternalId,
            rawText: incoming.Text,
            receivedAt: incoming.ReceivedAt.UtcDateTime);
        await messageRepository.AddAsync(message, ct);
        await messageRepository.SaveChangesAsync(ct);

        var ticket = await ticketRepository.GetByIdAsync(pendingTicketId, ct);
        if (ticket is not null)
        {
            var updatedContext = string.IsNullOrEmpty(ticket.Context)
                ? incoming.Text
                : $"{ticket.Context}; {incoming.Text}";
            ticket.SetContext(updatedContext);

            // Remove MissingLocation from ambiguity reasons — resident just provided location.
            if (ticket.AmbiguityReasonsJson is not null && ticket.RoutingAction.HasValue)
            {
                var reasons = JsonSerializer.Deserialize<List<string>>(ticket.AmbiguityReasonsJson) ?? [];
                reasons.Remove("missing_location");
                ticket.SetRoutingDecision(
                    ticket.RoutingAction.Value,
                    reasons.Count > 0 ? JsonSerializer.Serialize(reasons) : null);
            }

            await ticketRepository.SaveChangesAsync(ct);

            var reply = ReplyTemplates.BuildTicketReply(ticket, resident.PreferredLanguage);
            await channel.SendAsync(incoming.SenderId, reply, ct);

            logger.LogInformation(
                "Clarification response processed for ticket {TicketId} from {SenderId}",
                pendingTicketId, incoming.SenderId);
        }
        else
        {
            logger.LogWarning(
                "PendingClarificationTicketId {TicketId} not found for resident {ResidentId}",
                pendingTicketId, resident.Id);
        }

        resident.SetPendingClarification(null);
        await residentRepository.SaveChangesAsync(ct);

        message.MarkProcessed();
        await messageRepository.SaveChangesAsync(ct);
    }

    private static string GetWelcomeMessage(string lang) => lang == "en" ? """
        👋 Hello! I'm Hanwas AI.
        Just describe your maintenance issue — our system will assess and route it to your building manager automatically.

        📌 You can report:
        · Water leaks, electrical faults, elevator issues
        · Common area problems
        · Emergencies
        · 📷 Photo: 1 image per message, max ~10 MB
        · 🎙️ Voice message: max 60 seconds

        ⚠️ Limits: 1 photo per message · Max ~10 MB · Voice max 60 s

        🔒 Messages are processed solely for maintenance management. (KVKK §6698)

        Please describe your issue 👇
        """ : """
        👋 Merhaba! Ben Hanwas AI.
        Apartmanınızdaki arıza ve bakım taleplerinizi buraya yazmanız yeterli — sistemimiz talebinizi otomatik olarak değerlendirip yöneticinize iletecek.

        📌 Bildirebilecekleriniz:
        · Su kaçağı, elektrik arızası, asansör
        · Ortak alan sorunları
        · Acil durumlar
        · 📷 Fotoğraf: mesaj başına 1 görsel, maks. ~10 MB
        · 🎙️ Ses kaydı: maks. 60 saniye

        ⚠️ Sınırlar: 1 fotoğraf / mesaj · Maks. ~10 MB · Ses maks. 60 sn

        🔒 Mesajlarınız yalnızca bakım yönetimi amacıyla işlenmektedir. (KVKK md. 6698)

        Talebinizi yazabilirsiniz 👇
        """;

    private static string DetectLanguage(string? languageCode, string text)
    {
        if (languageCode == "tr") return "tr";
        if (text.Any(c => "çğıöşüÇĞİÖŞÜ".Contains(c))) return "tr";
        return "en";
    }

    private async Task<Resident> CreateResidentAsync(long telegramId, string preferredLanguage, CancellationToken ct)
    {
        var resident = Resident.Create(telegramId: telegramId, preferredLanguage: preferredLanguage);
        await residentRepository.AddAsync(resident, ct);
        await residentRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Auto-created resident {ResidentId} for Telegram {TelegramId} (lang={Language})",
            resident.Id, telegramId, preferredLanguage);

        try
        {
            await channel.SendAsync(telegramId.ToString(), GetWelcomeMessage(preferredLanguage), ct);
            logger.LogInformation(
                "Sent welcome message to Telegram {TelegramId}", telegramId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to send welcome message to Telegram {TelegramId}", telegramId);
        }

        return resident;
    }
}
