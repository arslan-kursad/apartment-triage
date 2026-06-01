using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Web.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Jobs;

public class WhatsAppConsumerJobTests
{
    [Fact]
    public async Task RunAsync_ExistingResidentWithPlus_LookupWithoutPlus_DoesNotCreateDuplicate()
    {
        var sentMessages = new List<(string RecipientId, string Text)>();
        var channel = new FakeWhatsAppChannel(sentMessages, senderId: "905550001234");
        var existing = Resident.Create(whatsAppNumber: "+905550001234");
        var residentRepository = new NormalizingResidentRepository(existing);
        var messageRepository = new FakeMessageRepository();
        var orchestrator = new FakeTriageOrchestrator();

        var job = new WhatsAppConsumerJob(
            channel,
            residentRepository,
            messageRepository,
            orchestrator,
            NullLogger<WhatsAppConsumerJob>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await job.RunAsync(cts.Token);

        residentRepository.AddedResidents.Should().BeEmpty();
        messageRepository.AddedMessages.Should().ContainSingle();
        sentMessages.Should().ContainSingle();
    }
}

internal sealed class FakeWhatsAppChannel : IMessageChannel
{
    private readonly List<(string RecipientId, string Text)> _sentMessages;
    private readonly string _senderId;

    public FakeWhatsAppChannel(List<(string RecipientId, string Text)> sentMessages, string senderId)
    {
        _sentMessages = sentMessages;
        _senderId = senderId;
    }

    public ChannelType ChannelType => ChannelType.WhatsApp;

    public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new IncomingMessage(
            ExternalId: "wa-mock-001",
            SenderId: _senderId,
            Text: "Su kaçağı var",
            ReceivedAt: DateTime.UtcNow);
        await Task.CompletedTask;
    }

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        _sentMessages.Add((recipientId, text));
        return Task.CompletedTask;
    }
}

/// <summary>Matches production lookup: normalize before compare.</summary>
internal sealed class NormalizingResidentRepository : IResidentRepository
{
    private readonly List<Resident> _residents = new();

    public NormalizingResidentRepository(params Resident[] seed) => _residents.AddRange(seed);

    public List<Resident> AddedResidents { get; } = new();

    public Task<Resident?> FindByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
        => Task.FromResult(_residents.Find(r => r.TelegramId == telegramId));

    public Task<Resident?> FindByWhatsAppNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        var normalized = PhoneNumberNormalizer.Normalize(number);
        return Task.FromResult(_residents.Find(r => r.WhatsAppNumber == normalized));
    }

    public Task<Resident?> FindByContactPhoneAsync(string number, CancellationToken cancellationToken = default)
    {
        var normalized = PhoneNumberNormalizer.Normalize(number);
        return Task.FromResult(_residents.Find(r => r.ContactPhone == normalized));
    }

    public Task AddAsync(Resident resident, CancellationToken cancellationToken = default)
    {
        AddedResidents.Add(resident);
        _residents.Add(resident);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
