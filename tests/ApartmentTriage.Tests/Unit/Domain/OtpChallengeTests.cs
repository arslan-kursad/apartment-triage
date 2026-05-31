using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Domain;

public class OtpChallengeTests
{
    private const int MaxAttempts = 5;

    private static OtpChallenge NewChallenge(DateTime expiresAt)
        => OtpChallenge.Create("8013067042", ChannelType.Telegram, "HASH", expiresAt);

    [Fact]
    public void Create_FreshChallenge_IsRedeemable()
    {
        var now = DateTime.UtcNow;
        var challenge = NewChallenge(now.AddMinutes(5));

        challenge.IsConsumed.Should().BeFalse();
        challenge.AttemptCount.Should().Be(0);
        challenge.IsRedeemable(now, MaxAttempts).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_PastExpiry_True()
    {
        var now = DateTime.UtcNow;
        var challenge = NewChallenge(now.AddMinutes(-1));

        challenge.IsExpired(now).Should().BeTrue();
        challenge.IsRedeemable(now, MaxAttempts).Should().BeFalse();
    }

    [Fact]
    public void Consume_MarksConsumed_NotRedeemable()
    {
        var now = DateTime.UtcNow;
        var challenge = NewChallenge(now.AddMinutes(5));

        challenge.Consume();

        challenge.IsConsumed.Should().BeTrue();
        challenge.ConsumedAt.Should().NotBeNull();
        challenge.IsRedeemable(now, MaxAttempts).Should().BeFalse();
    }

    [Fact]
    public void RegisterAttempt_AtMax_NotRedeemable()
    {
        var now = DateTime.UtcNow;
        var challenge = NewChallenge(now.AddMinutes(5));

        for (var i = 0; i < MaxAttempts; i++)
            challenge.RegisterAttempt();

        challenge.AttemptCount.Should().Be(MaxAttempts);
        challenge.IsRedeemable(now, MaxAttempts).Should().BeFalse();   // attempt ceiling reached
    }
}
