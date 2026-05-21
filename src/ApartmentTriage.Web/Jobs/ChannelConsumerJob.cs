using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentTriage.Web.Jobs;

/// <summary>
/// Polls the Telegram channel once per minute, runs triage for each new message,
/// and sends clarification replies when the orchestrator signals ambiguity (Option C).
/// </summary>
public sealed class ChannelConsumerJob(
    [FromKeyedServices(ChannelType.Telegram)] IMessageChannel channel,
    IResidentRepository residentRepository,
    IMessageRepository messageRepository,
    ITriageOrchestrator orchestrator,
    ILogger<ChannelConsumerJob> logger)
{
    // 55s budget per job execution: allows one 30s Telegram long-poll to complete,
    // processes results, then starts a second poll before the next 60s trigger fires.
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

        // Option C: orchestrator is channel-agnostic; caller sends clarification.
        var clarification = ClarificationTemplates.BuildMessage(result.AmbiguityReasons);
        if (clarification is not null)
        {
            logger.LogInformation(
                "Sending clarification to {SenderId} — reasons: {Reasons}",
                incoming.SenderId, string.Join(", ", result.AmbiguityReasons));

            await channel.SendAsync(incoming.SenderId, clarification, ct);
        }
    }

    private async Task<Resident> CreateResidentAsync(long telegramId, CancellationToken ct)
    {
        var resident = Resident.Create(telegramId: telegramId);
        await residentRepository.AddAsync(resident, ct);
        await residentRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Auto-created resident {ResidentId} for Telegram {TelegramId}",
            resident.Id, telegramId);

        return resident;
    }
}
