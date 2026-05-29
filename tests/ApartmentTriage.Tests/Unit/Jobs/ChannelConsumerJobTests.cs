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
            new FakeTicketRepository(),
            orchestrator,
            NullLogger<ChannelConsumerJob>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await job.RunAsync(cts.Token);

        sentMessages.Should().HaveCount(2);
        sentMessages[0].RecipientId.Should().Be("8013067042");
        sentMessages[0].Text.Should().Contain("Hanwas");
        sentMessages[0].Text.Should().Contain("Talebinizi yazabilirsiniz");
        sentMessages[1].RecipientId.Should().Be("8013067042");
        sentMessages[1].Text.Should().Contain("Talebinizi aldık.");

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
            new FakeTicketRepository(),
            orchestrator,
            NullLogger<ChannelConsumerJob>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await job.RunAsync(cts.Token);

        sentMessages.Should().ContainSingle();
        sentMessages[0].RecipientId.Should().Be("8013067042");
        sentMessages[0].Text.Should().Contain("Talebinizi aldık.");

        residentRepository.AddedResidents.Should().BeEmpty();
        messageRepository.AddedMessages.Should().ContainSingle();
    }
}

public class ClarificationTemplatesTests
{
    [Theory]
    [InlineData("tr", "🤔 Talebinizi aldık.")]
    [InlineData("en", "🤔 Got your request.")]
    public void BuildMessage_ReturnsEmojiWrapper_ForBothLanguages(string lang, string expectedPrefix)
    {
        var reasons = new[] { AmbiguityReason.MissingLocation };
        var result = ClarificationTemplates.BuildMessage(reasons, lang);
        result.Should().NotBeNull();
        result!.Should().StartWith(expectedPrefix);
    }

    [Fact]
    public void BuildMessage_ReturnsNull_WhenNonActionableOnly()
    {
        var reasons = new[] { AmbiguityReason.NonActionable };
        ClarificationTemplates.BuildMessage(reasons, "tr").Should().BeNull();
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

internal sealed class FakeTicketRepository : ITicketRepository
{
    public Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult<Ticket?>(null);

    public Task AddAsync(Ticket ticket, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AddRangeAsync(IEnumerable<Ticket> tickets, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ApartmentTriage.Application.Agents.Enricher.SimilarTicket>> FindSimilarAsync(
        float[] vector, Guid excludeTicketId, int topK = 5, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ApartmentTriage.Application.Agents.Enricher.SimilarTicket>>(Array.Empty<ApartmentTriage.Application.Agents.Enricher.SimilarTicket>());

    public Task<(IReadOnlyList<Ticket> Items, int TotalCount)> GetPagedAsync(
        ApartmentTriage.Domain.Enums.TicketStatus? status,
        ApartmentTriage.Domain.Enums.TicketCategory? category,
        bool? isEmergency, int page, int pageSize,
        CancellationToken cancellationToken = default)
        => Task.FromResult<(IReadOnlyList<Ticket>, int)>((Array.Empty<Ticket>(), 0));
}

internal sealed class FakeTriageOrchestrator : ITriageOrchestrator
{
    public Task<TriageResult> ProcessAsync(
        Message message,
        string preferredLanguage = "tr",
        byte[]? imageData = null,
        string? imageMimeType = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(TriageResult.Ok(
            new List<Ticket>(),
            replyText: preferredLanguage == "en"
                ? "✅ Your request has been received and recorded."
                : "✅ Talebinizi aldık. Sorununuzu sistemimize kaydettik; yöneticiniz en kısa sürede bilgilendirilecektir."));

    public Task<TriageResult> ReclassifyTicketAsync(
        Ticket ticket,
        string combinedText,
        string preferredLanguage = "tr",
        CancellationToken cancellationToken = default)
        => Task.FromResult(TriageResult.Ok(
            new List<Ticket> { ticket },
            replyText: preferredLanguage == "en"
                ? "✅ Your issue has been re-classified and recorded."
                : "✅ Talebiniz yeniden sınıflandırıldı ve kaydedildi."));
}
