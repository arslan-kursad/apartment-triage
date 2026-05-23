using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Web.Jobs;
using FluentAssertions;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Jobs;

public class ChannelConsumerJobTests
{
    [Fact]
    public async Task RunAsync_NewTelegramUser_SendsWelcomeMessage()
    {
        var sentMessages = new List<(string RecipientId, string Text)>();
        var channel = new FakeTelegramChannel(sentMessages);
        var residentRepository = new FakeResidentRepository();
        var messageRepository = new FakeMessageRepository();
        var orchestrator = new FakeTriageOrchestrator();

        var job = new ChannelConsumerJob(
            channel,
            residentRepository,
            messageRepository,
            orchestrator,
            NullLogger<ChannelConsumerJob>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await job.RunAsync(cts.Token);

        sentMessages.Should().HaveCount(2);
        sentMessages[0].RecipientId.Should().Be("8013067042");
        sentMessages[0].Text.Should().Contain("Almila Apartman'ın yapay zeka destekli yönetim sistemi artık");
        sentMessages[0].Text.Should().Contain("Hazır olduğunuzda yazabilirsiniz.");
        sentMessages[1].RecipientId.Should().Be("8013067042");
        sentMessages[1].Text.Should().Contain("Talebinizi aldık. Sorununuzu sistemimize kaydettik;");

        residentRepository.AddedResidents.Should().ContainSingle(r => r.TelegramId == 8013067042);
        messageRepository.AddedMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_ExistingTelegramUser_SendsAcknowledgementMessage()
    {
        var sentMessages = new List<(string RecipientId, string Text)>();
        var channel = new FakeTelegramChannel(sentMessages);
        var resident = Resident.Create(telegramId: 8013067042);
        var residentRepository = new FakeResidentRepository(existingResident: resident);
        var messageRepository = new FakeMessageRepository();
        var orchestrator = new FakeTriageOrchestrator();

        var job = new ChannelConsumerJob(
            channel,
            residentRepository,
            messageRepository,
            orchestrator,
            NullLogger<ChannelConsumerJob>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await job.RunAsync(cts.Token);

        sentMessages.Should().ContainSingle();
        sentMessages[0].RecipientId.Should().Be("8013067042");
        sentMessages[0].Text.Should().Contain("Talebinizi aldık. Sorununuzu sistemimize kaydettik;");

        residentRepository.AddedResidents.Should().BeEmpty();
        messageRepository.AddedMessages.Should().ContainSingle();
    }
}

internal sealed class FakeTelegramChannel : IMessageChannel
{
    private readonly List<(string RecipientId, string Text)> _sentMessages;

    public FakeTelegramChannel(List<(string RecipientId, string Text)> sentMessages)
    {
        _sentMessages = sentMessages;
    }

    public ChannelType ChannelType => ChannelType.Telegram;

    public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new IncomingMessage(
            ExternalId: "mock-001",
            SenderId: "8013067042",
            Text: "Merhaba, asansör bozuk.",
            ReceivedAt: DateTime.UtcNow);
        await Task.CompletedTask;
    }

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        _sentMessages.Add((recipientId, text));
        return Task.CompletedTask;
    }
}

internal sealed class FakeResidentRepository : IResidentRepository
{
    private readonly Resident? _existingResident;

    public FakeResidentRepository(Resident? existingResident = null)
    {
        _existingResident = existingResident;
    }

    public List<Resident> AddedResidents { get; } = new();

    public Task<Resident?> FindByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
        => Task.FromResult(_existingResident?.TelegramId == telegramId ? _existingResident : null);

    public Task<Resident?> FindByWhatsAppNumberAsync(string number, CancellationToken cancellationToken = default)
        => Task.FromResult<Resident?>(null);

    public Task AddAsync(Resident resident, CancellationToken cancellationToken = default)
    {
        AddedResidents.Add(resident);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeMessageRepository : IMessageRepository
{
    public List<Message> AddedMessages { get; } = new();

    public Task AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        AddedMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string externalMessageId, ChannelType channelType, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeTriageOrchestrator : ITriageOrchestrator
{
    public Task<TriageResult> ProcessAsync(Message message, CancellationToken cancellationToken = default)
        => Task.FromResult(TriageResult.Ok(new List<Ticket>()));
}
