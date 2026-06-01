using ApartmentTriage.Web.Security;
using FluentAssertions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Security;

public class TelegramLoginCommandTests
{
    [Theory]
    [InlineData("/login", true)]
    [InlineData("/LOGIN", true)]
    [InlineData("/login@apartman_triage_bot", true)]
    [InlineData("/login@HanwasBot", true)]
    [InlineData("  /login  ", true)]
    [InlineData("/login extra", false)]
    [InlineData("/logins", false)]
    [InlineData("/start", false)]
    [InlineData(null, false)]
    public void IsLoginCommand_MatchesTelegramVariants(string? text, bool expected)
        => TelegramLoginCommand.IsLoginCommand(text).Should().Be(expected);
}
