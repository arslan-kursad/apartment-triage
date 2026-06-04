using System.Text.RegularExpressions;
using ApartmentTriage.Application.Channels;
using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Application.Services;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Services;

public class OtpServiceTests
{
    private const long ManagerTelegramId = 8013067042;

    // ── GenerateAndSend ────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAndSend_AuthorizedManager_PersistsChallengeAndSendsCode()
    {
        var (svc, otp, _, sent) = CreateService(Manager(ManagerTelegramId, "tr"));

        var status = await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);

        status.Should().Be(OtpSendStatus.Sent);
        otp.Challenges.Should().ContainSingle();
        otp.Challenges[0].Identifier.Should().Be(ManagerTelegramId.ToString());
        otp.Challenges[0].Channel.Should().Be(ChannelType.Telegram);
        otp.Challenges[0].CodeHash.Should().HaveLength(64);   // SHA-256 hex, never the plaintext

        sent.Should().ContainSingle();
        sent[0].Channel.Should().Be(ChannelType.Telegram);
        sent[0].RecipientId.Should().Be(ManagerTelegramId.ToString());
        sent[0].Text.Should().Contain("Hanwas giriş kodunuz");
        ExtractCode(sent[0].Text).Should().HaveLength(6);
    }

    [Fact]
    public async Task GenerateAndSend_EnglishManager_SendsEnglishCopy()
    {
        var (svc, _, _, sent) = CreateService(Manager(ManagerTelegramId, "en"));

        await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);

        sent[0].Text.Should().Contain("Your Hanwas login code");
    }

    [Fact]
    public async Task GenerateAndSend_UnknownIdentifier_Unauthorized_NoSendNoPersist()
    {
        var (svc, otp, _, sent) = CreateService(/* no residents */);

        var status = await svc.GenerateAndSendAsync("999999", ChannelType.Telegram);

        status.Should().Be(OtpSendStatus.Unauthorized);
        otp.Challenges.Should().BeEmpty();
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAndSend_RoleNone_Unauthorized_NoSendNoPersist()
    {
        // Plain resident (default Role.None) — enumeration-safe: behaves like an unknown identifier.
        var (svc, otp, _, sent) = CreateService(Resident.Create(telegramId: ManagerTelegramId));

        var status = await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);

        status.Should().Be(OtpSendStatus.Unauthorized);
        otp.Challenges.Should().BeEmpty();
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAndSend_SplitRecord_BootstrapPhoneManager_SendsOtp()
    {
        const long managerTelegramId = 100000001;
        const string phone = "+905550001234";
        var ghost = Resident.Create(telegramId: managerTelegramId, displayName: "Test Manager");
        var manager = Resident.Create(whatsAppNumber: phone, displayName: "Test Manager");
        manager.SetRole(ResidentRole.Manager);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:BootstrapManagerIdentifier"] = managerTelegramId.ToString(),
                ["Auth:BootstrapManagerPhone"] = phone,
                ["Auth:OtpExpiryMinutes"] = "5",
                ["Auth:MaxAttempts"] = "5",
                ["Auth:MaxChallengesPerWindow"] = "3",
                ["Auth:ChallengeWindowMinutes"] = "15"
            })
            .Build();

        var (svc, otp, _, sent) = CreateService(config, ghost, manager);

        var status = await svc.GenerateAndSendAsync(managerTelegramId.ToString(), ChannelType.Telegram);

        status.Should().Be(OtpSendStatus.Sent);
        otp.Challenges.Should().ContainSingle();
        sent.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateAndSend_InactiveManager_Unauthorized()
    {
        var manager = Manager(ManagerTelegramId, "tr");
        manager.Deactivate();
        var (svc, otp, _, sent) = CreateService(manager);

        var status = await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);

        status.Should().Be(OtpSendStatus.Unauthorized);
        otp.Challenges.Should().BeEmpty();
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAndSend_AtWindowMax_RateLimited_NoNewChallenge()
    {
        var (svc, otp, _, sent) = CreateService(Manager(ManagerTelegramId, "tr"));
        otp.ForcedCountSince = 3;   // == MaxChallengesPerWindow

        var status = await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);

        status.Should().Be(OtpSendStatus.RateLimited);
        otp.Challenges.Should().BeEmpty();           // no new code persisted
        sent.Should().ContainSingle();               // a rate-limit notice is sent
        sent[0].Text.Should().Contain("Çok fazla giriş");
    }

    // ── Verify ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_CorrectCode_Success_ConsumesChallenge()
    {
        var (svc, otp, _, sent) = CreateService(Manager(ManagerTelegramId, "tr"));
        await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);
        var code = ExtractCode(sent[0].Text);

        var result = await svc.VerifyAsync(code);

        result.Status.Should().Be(OtpVerifyStatus.Success);
        result.Resident.Should().NotBeNull();
        result.Resident!.TelegramId.Should().Be(ManagerTelegramId);
        otp.Challenges[0].IsConsumed.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_WrongCode_Invalid()
    {
        var (svc, _, _, sent) = CreateService(Manager(ManagerTelegramId, "tr"));
        await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);
        var code = ExtractCode(sent[0].Text);
        var wrong = code == "000000" ? "111111" : "000000";

        var result = await svc.VerifyAsync(wrong);

        result.Status.Should().Be(OtpVerifyStatus.Invalid);
        result.Resident.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Verify_BlankCode_Invalid(string code)
    {
        var (svc, _, _, _) = CreateService(Manager(ManagerTelegramId, "tr"));

        var result = await svc.VerifyAsync(code);

        result.Status.Should().Be(OtpVerifyStatus.Invalid);
    }

    [Fact]
    public async Task Verify_ExpiredCode_ReturnsExpired()
    {
        // Expiry = 0 → the code is born already past its expiry.
        var (svc, _, _, sent) = CreateService(BuildConfig(expiryMinutes: 0), Manager(ManagerTelegramId, "tr"));
        await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);
        var code = ExtractCode(sent[0].Text);

        var result = await svc.VerifyAsync(code);

        result.Status.Should().Be(OtpVerifyStatus.Expired);
    }

    [Fact]
    public async Task Verify_ReusedCode_SecondAttemptInvalid()
    {
        var (svc, _, _, sent) = CreateService(Manager(ManagerTelegramId, "tr"));
        await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);
        var code = ExtractCode(sent[0].Text);

        (await svc.VerifyAsync(code)).Status.Should().Be(OtpVerifyStatus.Success);
        (await svc.VerifyAsync(code)).Status.Should().Be(OtpVerifyStatus.Invalid);   // single-use
    }

    [Fact]
    public async Task Verify_TwoActiveChallengesShareCode_TooManyMatches()
    {
        var manager = Manager(ManagerTelegramId, "tr");
        var other = Manager(555, "tr");
        var (svc, otp, _, sent) = CreateService(manager, other);

        await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);
        var code = ExtractCode(sent[0].Text);

        // Forge a second active challenge for a different identifier that collides on the hash.
        otp.Challenges.Add(OtpChallenge.Create(
            identifier: "555",
            channel: ChannelType.Telegram,
            codeHash: otp.Challenges[0].CodeHash,
            expiresAt: DateTime.UtcNow.AddMinutes(5)));

        var result = await svc.VerifyAsync(code);

        result.Status.Should().Be(OtpVerifyStatus.TooManyMatches);
        result.Resident.Should().BeNull();
    }

    [Fact]
    public async Task Verify_RoleRevokedAfterIssue_Invalid_DoesNotConsume()
    {
        var manager = Manager(ManagerTelegramId, "tr");
        var (svc, otp, _, sent) = CreateService(manager);
        await svc.GenerateAndSendAsync(ManagerTelegramId.ToString(), ChannelType.Telegram);
        var code = ExtractCode(sent[0].Text);

        manager.SetRole(ResidentRole.None);   // demoted between issue and verify

        var result = await svc.VerifyAsync(code);

        result.Status.Should().Be(OtpVerifyStatus.Invalid);
        otp.Challenges[0].IsConsumed.Should().BeFalse();   // not burned — can expire naturally
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static Resident Manager(long telegramId, string lang)
    {
        var r = Resident.Create(telegramId: telegramId, preferredLanguage: lang);
        r.SetRole(ResidentRole.Manager);
        return r;
    }

    private static string ExtractCode(string message) => Regex.Match(message, @"\d{6}").Value;

    private static (OtpService Service, FakeOtpChallengeRepository Otp, FakeResidentStore Residents, List<SentMessage> Sent)
        CreateService(params Resident[] residents)
        => CreateService(BuildConfig(), residents);

    private static (OtpService Service, FakeOtpChallengeRepository Otp, FakeResidentStore Residents, List<SentMessage> Sent)
        CreateService(IConfiguration config, params Resident[] residents)
    {
        var otp = new FakeOtpChallengeRepository();
        var store = new FakeResidentStore(residents);
        var sent = new List<SentMessage>();
        var svc = new OtpService(
            otp,
            store,
            new RecordingChannel(ChannelType.Telegram, sent),
            new RecordingChannel(ChannelType.WhatsApp, sent),
            config,
            NullLogger<OtpService>.Instance);
        return (svc, otp, store, sent);
    }

    private static IConfiguration BuildConfig(
        int expiryMinutes = 5, int maxAttempts = 5, int maxPerWindow = 3, int windowMinutes = 15)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:OtpExpiryMinutes"] = expiryMinutes.ToString(),
                ["Auth:MaxAttempts"] = maxAttempts.ToString(),
                ["Auth:MaxChallengesPerWindow"] = maxPerWindow.ToString(),
                ["Auth:ChallengeWindowMinutes"] = windowMinutes.ToString()
            })
            .Build();
}

internal sealed record SentMessage(ChannelType Channel, string RecipientId, string Text);

internal sealed class RecordingChannel(ChannelType channelType, List<SentMessage> sent) : IMessageChannel
{
    public ChannelType ChannelType => channelType;

    public IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task SendAsync(string recipientId, string text, CancellationToken cancellationToken = default)
    {
        sent.Add(new SentMessage(channelType, recipientId, text));
        return Task.CompletedTask;
    }
}

internal sealed class FakeResidentStore(IEnumerable<Resident> residents) : IResidentRepository
{
    private readonly List<Resident> _residents = residents.ToList();

    public Task<Resident?> FindByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
        => Task.FromResult(_residents.FirstOrDefault(r => r.TelegramId == telegramId));

    public Task<Resident?> FindByWhatsAppNumberAsync(string number, CancellationToken cancellationToken = default)
        => Task.FromResult(_residents.FirstOrDefault(r => r.WhatsAppNumber == number));

    public Task<Resident?> FindByContactPhoneAsync(string number, CancellationToken cancellationToken = default)
        => Task.FromResult(_residents.FirstOrDefault(r => r.ContactPhone == number));

    public Task AddAsync(Resident resident, CancellationToken cancellationToken = default)
    {
        _residents.Add(resident);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeOtpChallengeRepository : IOtpChallengeRepository
{
    public List<OtpChallenge> Challenges { get; } = new();

    /// <summary>When set, overrides <see cref="CountSinceAsync"/> to force the rate-limit branch.</summary>
    public int? ForcedCountSince { get; set; }

    public Task AddAsync(OtpChallenge challenge, CancellationToken cancellationToken = default)
    {
        Challenges.Add(challenge);
        return Task.CompletedTask;
    }

    public Task<int> CountSinceAsync(
        string identifier, ChannelType channel, DateTime since, CancellationToken cancellationToken = default)
    {
        if (ForcedCountSince.HasValue) return Task.FromResult(ForcedCountSince.Value);
        return Task.FromResult(
            Challenges.Count(c => c.Identifier == identifier && c.Channel == channel && c.CreatedAt >= since));
    }

    public Task<IReadOnlyList<OtpChallenge>> FindUnconsumedByCodeHashAsync(
        string codeHash, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OtpChallenge> matches =
            Challenges.Where(c => c.CodeHash == codeHash && c.ConsumedAt == null).ToList();
        return Task.FromResult(matches);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
