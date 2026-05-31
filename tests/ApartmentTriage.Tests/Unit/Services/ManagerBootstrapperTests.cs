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

    private static ManagerBootstrapper Build(string? bootstrapIdentifier, params Resident[] residents)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:BootstrapManagerIdentifier"] = bootstrapIdentifier
            })
            .Build();

        return new ManagerBootstrapper(
            new FakeResidentStore(residents),
            config,
            NullLogger<ManagerBootstrapper>.Instance);
    }
}
