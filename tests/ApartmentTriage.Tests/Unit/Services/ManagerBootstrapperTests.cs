using ApartmentTriage.Application.Repositories;
using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using ApartmentTriage.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Services;

public class ManagerBootstrapperTests
{
    private const long KursadTelegramId = 100000001;

    [Fact]
    public async Task ConfiguredResidentExists_PromotedToManager()
    {
        var resident = Resident.Create(telegramId: KursadTelegramId, displayName: "Kürşad");
        resident.Role.Should().Be(ResidentRole.None);
        var bootstrapper = Build(KursadTelegramId.ToString(), resident);

        await bootstrapper.RunAsync();

        resident.Role.Should().Be(ResidentRole.Manager);
    }

    [Fact]
    public async Task AlreadyManager_NoChange()
    {
        var resident = Resident.Create(telegramId: KursadTelegramId);
        resident.SetRole(ResidentRole.Manager);
        var bootstrapper = Build(KursadTelegramId.ToString(), resident);

        await bootstrapper.RunAsync();

        resident.Role.Should().Be(ResidentRole.Manager);
    }

    [Fact]
    public async Task ResidentNotFound_NoThrow()
    {
        // Configured ID present, but no such resident yet (hasn't messaged the bot).
        var bootstrapper = Build(KursadTelegramId.ToString() /* no residents */);

        var act = async () => await bootstrapper.RunAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotConfigured_NoOp_OtherResidentsUntouched()
    {
        var someone = Resident.Create(telegramId: KursadTelegramId);
        var bootstrapper = Build(bootstrapIdentifier: "", someone);

        await bootstrapper.RunAsync();

        someone.Role.Should().Be(ResidentRole.None);
    }

    [Fact]
    public async Task InvalidIdentifier_NoOp()
    {
        var someone = Resident.Create(telegramId: KursadTelegramId);
        var bootstrapper = Build("@not-a-telegram-id", someone);

        await bootstrapper.RunAsync();

        someone.Role.Should().Be(ResidentRole.None);
    }

    [Fact]
    public async Task SplitWhatsAppManager_LinksTelegramAndDeactivatesGhost()
    {
        const string phone = "+905550001234";
        var ghost = Resident.Create(telegramId: KursadTelegramId, displayName: "Kürşad");
        var manager = Resident.Create(whatsAppNumber: phone, displayName: "KÜRŞAD");
        manager.SetRole(ResidentRole.Manager);

        var bootstrapper = Build(KursadTelegramId.ToString(), phone, ghost, manager);

        await bootstrapper.RunAsync();

        manager.TelegramId.Should().Be(KursadTelegramId);
        manager.Role.Should().Be(ResidentRole.Manager);
        ghost.TelegramId.Should().BeNull();
        ghost.IsActive.Should().BeFalse();
    }

    private static ManagerBootstrapper Build(
        string? bootstrapIdentifier,
        params Resident[] residents)
        => Build(bootstrapIdentifier, bootstrapPhone: null, residents);

    private static ManagerBootstrapper Build(
        string? bootstrapIdentifier,
        string? bootstrapPhone,
        params Resident[] residents)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:BootstrapManagerIdentifier"] = bootstrapIdentifier,
                ["Auth:BootstrapManagerPhone"] = bootstrapPhone
            })
            .Build();

        return new ManagerBootstrapper(
            new BootstrapFakeResidentStore(residents),
            config,
            NullLogger<ManagerBootstrapper>.Instance);
    }
}

internal sealed class BootstrapFakeResidentStore(IEnumerable<Resident> residents) : IResidentRepository
{
    private readonly List<Resident> _residents = residents.ToList();

    public Task<Resident?> FindByTelegramIdAsync(long telegramId, CancellationToken cancellationToken = default)
        => Task.FromResult(_residents.FirstOrDefault(r => r.TelegramId == telegramId && r.IsActive));

    public Task<Resident?> FindByWhatsAppNumberAsync(string number, CancellationToken cancellationToken = default)
        => Task.FromResult(_residents.FirstOrDefault(r => r.WhatsAppNumber == number));

    public Task<Resident?> FindByContactPhoneAsync(string number, CancellationToken cancellationToken = default)
        => Task.FromResult(_residents.FirstOrDefault(r => r.ContactPhone == number));

    public Task AddAsync(Resident resident, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
