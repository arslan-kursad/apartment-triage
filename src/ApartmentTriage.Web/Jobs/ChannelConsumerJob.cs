// SKELETON — not wired to Hangfire yet.
// Demonstrates the clarification flow integration (Option C, Day 8).
// Full registration pending Day 9: resident resolution + message persistence.

using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Web.Jobs;

/// <summary>
/// Reads messages from a channel, runs triage, and sends clarification replies when needed.
/// Clarification is sent by the caller (Option C) — orchestrator stays channel-agnostic.
/// </summary>
public sealed class ChannelConsumerJob(
    IMessageChannel channel,
    ITriageOrchestrator orchestrator,
    ILogger<ChannelConsumerJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await foreach (var incoming in channel.ReadMessagesAsync(ct))
        {
            // TODO Day 9: resolve Resident, persist as Message entity, handle idempotency
            var message = BuildPlaceholderMessage(incoming, channel.ChannelType);

            var result = await orchestrator.ProcessAsync(message, ct);

            if (!result.IsSuccess)
            {
                logger.LogWarning(
                    "Triage failed for {ExternalId}: {Error}",
                    incoming.ExternalId, result.Error!.Message);
                continue;
            }

            // Option C: caller sends clarification — orchestrator does not know the channel.
            // incoming.SenderId = channel-native recipient ID (Telegram chat_id, WhatsApp E.164).
            var clarification = ClarificationTemplates.BuildMessage(result.AmbiguityReasons);
            if (clarification is not null)
            {
                logger.LogInformation(
                    "Sending clarification to {SenderId} ({Reasons})",
                    incoming.SenderId,
                    string.Join(", ", result.AmbiguityReasons));

                await channel.SendAsync(incoming.SenderId, clarification, ct);
            }
        }
    }

    // Temporary bridge until full ingestion pipeline (resident registry + Message persistence) lands.
    private static Message BuildPlaceholderMessage(IncomingMessage incoming, ChannelType channelType) =>
        Message.Create(
            residentId: Guid.Empty,
            channelType: channelType,
            externalMessageId: incoming.ExternalId,
            rawText: incoming.Text,
            receivedAt: incoming.ReceivedAt.UtcDateTime);
}
