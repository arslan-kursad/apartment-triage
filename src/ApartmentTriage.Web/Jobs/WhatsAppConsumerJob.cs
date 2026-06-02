using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Web.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace ApartmentTriage.Web.Jobs;

/// <summary>
/// Drains the WhatsApp BoundedChannel (filled by webhook) and runs triage per message.
/// Unlike TelegramConsumerJob, no long-poll — webhook is push-based, budget is 10s.
/// </summary>
public sealed class WhatsAppConsumerJob(
    [FromKeyedServices(ChannelType.WhatsApp)] IMessageChannel channel,
    IResidentRepository residentRepository,
    IMessageRepository messageRepository,
    ITriageOrchestrator orchestrator,
    ILogger<WhatsAppConsumerJob> logger)
{
    // 10s drain window: long enough to clear backlog accumulated between minute ticks,
    // short enough not to stall the Hangfire worker thread.
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
            // Normal: 10s drain window exhausted (channel empty or budget hit).
        }
    }

    private async Task ProcessIncomingAsync(IncomingMessage incoming, CancellationToken ct)
    {
        if (await messageRepository.ExistsAsync(incoming.ExternalId, channel.ChannelType, ct))
        {
            logger.LogDebug("Skipping duplicate WhatsApp message {ExternalId}", incoming.ExternalId);
            return;
        }

        // WhatsApp carries no app-language code → detect purely from message content.
        var messageLang = LanguageDetector.Detect(incoming.Text, incoming.LanguageCode);

        var resident = await residentRepository.FindByWhatsAppNumberAsync(incoming.SenderId, ct)
            ?? await CreateResidentAsync(incoming.SenderId, messageLang, ct);

        if (incoming.SenderName is not null && resident.DisplayName is null)
            resident.UpdateContactInfo(displayName: incoming.SenderName);

        // Reply in the language of THIS message.
        if (resident.PreferredLanguage != messageLang)
            resident.SetPreferredLanguage(messageLang);

        var message = Message.Create(
            residentId: resident.Id,
            channelType: channel.ChannelType,
            externalMessageId: incoming.ExternalId,
            rawText: incoming.Text,
            receivedAt: incoming.ReceivedAt.UtcDateTime);

        await messageRepository.AddAsync(message, ct);
        await messageRepository.SaveChangesAsync(ct);

        var result = await orchestrator.ProcessAsync(
            message, resident.PreferredLanguage, cancellationToken: ct);

        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Triage failed for WhatsApp {ExternalId}: {Error}",
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
        }
    }

    private async Task<Resident> CreateResidentAsync(string whatsAppNumber, string preferredLanguage, CancellationToken ct)
    {
        var resident = Resident.Create(whatsAppNumber: whatsAppNumber, preferredLanguage: preferredLanguage);
        await residentRepository.AddAsync(resident, ct);
        await residentRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Auto-created resident {ResidentId} for WhatsApp {WhatsAppNumber}",
            resident.Id, whatsAppNumber);

        return resident;
    }
}
