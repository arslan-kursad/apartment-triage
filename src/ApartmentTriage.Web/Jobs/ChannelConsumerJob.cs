using System.Text.Json;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Application.Services;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Infrastructure.Services;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Web.Helpers;
using ApartmentTriage.Web.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentTriage.Web.Jobs;

public sealed class ChannelConsumerJob(
    [FromKeyedServices(ChannelType.Telegram)] IMessageChannel channel,
    IResidentRepository residentRepository,
    IMessageRepository messageRepository,
    ITicketRepository ticketRepository,
    IOtpService otpService,
    ManagerBootstrapper managerBootstrapper,
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

        if (IsLoginCommand(incoming.Text))
        {
            await HandleLoginCommandAsync(incoming, ct);
            return;
        }

        var messageLang = DetectLanguage(incoming.LanguageCode, incoming.Text);
        var resident = await residentRepository.FindByTelegramIdAsync(telegramId, ct)
            ?? await CreateResidentAsync(telegramId, messageLang, ct);

        if (incoming.SenderName is not null && resident.DisplayName is null)
            resident.UpdateContactInfo(displayName: incoming.SenderName);

        // Reply in the language of THIS message — a Turkish-locale user may write in English.
        if (resident.PreferredLanguage != messageLang)
            resident.SetPreferredLanguage(messageLang);

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
            receivedAt: incoming.ReceivedAt.UtcDateTime,
            imageData: incoming.ImageData,
            imageMimeType: incoming.ImageMimeType);

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
            // Append clarification text to ticket context
            var updatedContext = string.IsNullOrEmpty(ticket.Context)
                ? incoming.Text
                : $"{ticket.Context}; {incoming.Text}";
            ticket.SetContext(updatedContext);

            // Deserialize ambiguity reasons; check before mutating.
            // AmbiguityReasonsJson is serialized as integer array (default System.Text.Json enum behavior).
            var reasons = ticket.AmbiguityReasonsJson is not null
                ? JsonSerializer.Deserialize<List<AmbiguityReason>>(ticket.AmbiguityReasonsJson) ?? []
                : new List<AmbiguityReason>();

            var hasCategoryAmbiguous = reasons.Contains(AmbiguityReason.CategoryAmbiguous);

            // Remove MissingLocation — resident just provided location/context.
            reasons.Remove(AmbiguityReason.MissingLocation);
            if (ticket.RoutingAction.HasValue)
            {
                ticket.SetRoutingDecision(
                    ticket.RoutingAction.Value,
                    reasons.Count > 0 ? JsonSerializer.Serialize(reasons) : null);
            }

            await ticketRepository.SaveChangesAsync(ct);

            string reply;

            if (hasCategoryAmbiguous)
            {
                // CategoryAmbiguous present → full pipeline re-triage on this ticket.
                // combinedText = original raw text + clarification text.
                var originalText = ticket.SourceMessage?.RawText ?? string.Empty;
                var combinedText = string.IsNullOrEmpty(originalText)
                    ? incoming.Text
                    : $"{originalText} {incoming.Text}";

                var reTriageResult = await orchestrator.ReclassifyTicketAsync(
                    ticket, combinedText, resident.PreferredLanguage, ct);

                if (reTriageResult.IsSuccess && reTriageResult.ReplyText is not null)
                {
                    reply = reTriageResult.ReplyText;
                    logger.LogInformation(
                        "Re-triage complete for ticket {TicketId}: Category={Category}, Severity={Severity}",
                        pendingTicketId, ticket.Category, ticket.Severity);
                }
                else
                {
                    reply = ReplyTemplates.BuildTicketReply(ticket, resident.PreferredLanguage);
                    logger.LogWarning(
                        "Re-triage failed or produced no reply for ticket {TicketId}: {Error}",
                        pendingTicketId, reTriageResult.Error?.Message);
                }
            }
            else
            {
                reply = ReplyTemplates.BuildTicketReply(ticket, resident.PreferredLanguage);
                logger.LogInformation(
                    "Clarification response processed for ticket {TicketId} from {SenderId}",
                    pendingTicketId, incoming.SenderId);
            }

            await channel.SendAsync(incoming.SenderId, reply, ct);
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

    private async Task HandleLoginCommandAsync(IncomingMessage incoming, CancellationToken ct)
    {
        if (long.TryParse(incoming.SenderId, out var telegramId))
            await EnsureTelegramResidentForLoginAsync(telegramId, incoming, ct);

        await managerBootstrapper.RunAsync(ct);

        var result = await otpService.GenerateAndSendAsync(incoming.SenderId, channel.ChannelType, ct);
        logger.LogInformation(
            "Telegram /login handled for {SenderId} with status {Status}",
            incoming.SenderId, result);

        if (result != OtpSendStatus.Unauthorized)
            return;

        var lang = DetectLanguage(incoming.LanguageCode, incoming.Text);
        await channel.SendAsync(incoming.SenderId, GetLoginUnauthorizedMessage(lang), ct);
    }

    private static string GetWelcomeMessage(string lang) => lang == "en" ? """
        👋 Hello! I'm Hanwas.
        Just describe your maintenance issue — our system will assess and route it to your building manager automatically.

        📌 You can report:
        · Water leaks, electrical faults, elevator issues
        · Common area problems
        · Emergencies
        · 📷 Photo: 1 image per message, max ~10 MB (JPEG/PNG/WebP/GIF)

        🔒 Messages are processed solely for maintenance management. (KVKK §6698)

        Please describe your issue 👇
        """ : """
        👋 Merhaba! Ben Hanwas.
        Apartmanınızdaki arıza ve bakım taleplerinizi buraya yazmanız yeterli — sistemimiz talebinizi otomatik olarak değerlendirip yöneticinize iletecek.

        📌 Bildirebilecekleriniz:
        · Su kaçağı, elektrik arızası, asansör
        · Ortak alan sorunları
        · Acil durumlar
        · 📷 Fotoğraf: mesaj başına 1 görsel, maks. ~10 MB (JPEG/PNG/WebP/GIF)

        🔒 Mesajlarınız yalnızca bakım yönetimi amacıyla işlenmektedir. (KVKK md. 6698)

        Talebinizi yazabilirsiniz 👇
        """;

    // Content-first detection (see LanguageDetector): the channel language_code is only a tiebreak.
    private static string DetectLanguage(string? languageCode, string text)
        => LanguageDetector.Detect(text, languageCode);

    private static bool IsLoginCommand(string text) => TelegramLoginCommand.IsLoginCommand(text);

    private static string GetLoginUnauthorizedMessage(string lang) => lang == "en"
        ? """
          ⚠️ Dashboard login is not available for this account.
          If you are a building manager, ask your administrator to grant access, then send /login again.
          """
        : """
          ⚠️ Bu hesap için panel girişi açık değil.
          Yönetici iseniz erişim atanmasını isteyin, ardından tekrar /login yazın.
          """;

    private async Task EnsureTelegramResidentForLoginAsync(
        long telegramId, IncomingMessage incoming, CancellationToken ct)
    {
        if (await residentRepository.FindByTelegramIdAsync(telegramId, ct) is not null)
            return;

        var preferredLanguage = DetectLanguage(incoming.LanguageCode, incoming.Text);
        var resident = Resident.Create(telegramId: telegramId, preferredLanguage: preferredLanguage);
        await residentRepository.AddAsync(resident, ct);
        await residentRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created resident {ResidentId} for Telegram {TelegramId} on /login (no welcome message)",
            resident.Id, telegramId);
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
