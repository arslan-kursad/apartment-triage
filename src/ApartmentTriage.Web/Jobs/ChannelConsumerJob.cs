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

        var resident = await residentRepository.FindByTelegramIdAsync(telegramId, ct)
            ?? await CreateResidentAsync(telegramId, ct);

        var message = Message.Create(
            residentId: resident.Id,
            channelType: channel.ChannelType,
            externalMessageId: incoming.ExternalId,
            rawText: incoming.Text,
            receivedAt: incoming.ReceivedAt.UtcDateTime);

        await messageRepository.AddAsync(message, ct);
        await messageRepository.SaveChangesAsync(ct);

        var result = await orchestrator.ProcessAsync(message, ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Triage failed for {ExternalId}: {Error}",
                incoming.ExternalId, result.Error!.Message);
            return;
        }

        message.MarkProcessed();
        await messageRepository.SaveChangesAsync(ct);

        var reply = ClarificationTemplates.BuildMessage(result.AmbiguityReasons)
            ?? ClarificationTemplates.BuildAcknowledgementMessage();

        logger.LogInformation(
            "Sending reply to {SenderId} — replyType: {ReplyType}",
            incoming.SenderId,
            result.AmbiguityReasons.Count > 0 ? "clarification" : "acknowledgement");

        await channel.SendAsync(incoming.SenderId, reply, ct);
    }

    private static readonly string WelcomeMessage = """
Merhaba 👋

Almila Apartman'ın yapay zeka destekli yönetim sistemi artık
Telegram üzerinden çalışıyor. Su kaçağı, elektrik arızası,
asansör sorunu ya da herhangi bir bakım talebini buraya
yazmanız yeterli.

📌 Nasıl kullanılır?
Sorununuzu kısa bir mesajla yazın; sistem talebinizi
değerlendirip yöneticinize iletir.

🔒 Gizlilik:
İlettiğiniz mesajlar yalnızca bakım ve şikayet yönetimi
amacıyla işlenmektedir.

Hazır olduğunuzda yazabilirsiniz.
""";

    private async Task<Resident> CreateResidentAsync(long telegramId, CancellationToken ct)
    {
        var resident = Resident.Create(telegramId: telegramId);
        await residentRepository.AddAsync(resident, ct);
        await residentRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Auto-created resident {ResidentId} for Telegram {TelegramId}",
            resident.Id, telegramId);

        try
        {
            await channel.SendAsync(telegramId.ToString(), WelcomeMessage, ct);
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
