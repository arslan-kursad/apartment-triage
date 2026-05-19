using ApartmentTriage.Application.Orchestration;
using ApartmentTriage.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Orchestration;

public class ClarificationTemplatesTests
{
    [Fact]
    public void EmptyReasons_ReturnsNull()
        => ClarificationTemplates.BuildMessage([]).Should().BeNull();

    [Fact]
    public void NonActionableOnly_ReturnsNull()
        => ClarificationTemplates.BuildMessage([AmbiguityReason.NonActionable]).Should().BeNull();

    [Fact]
    public void MissingLocation_ReturnsLocationMessage()
    {
        var msg = ClarificationTemplates.BuildMessage([AmbiguityReason.MissingLocation]);
        msg.Should().NotBeNull();
        msg.Should().Contain("hangi daire");
    }

    [Fact]
    public void MissingSeverity_ReturnsMessage()
        => ClarificationTemplates.BuildMessage([AmbiguityReason.MissingSeverity])
            .Should().NotBeNull();

    [Fact]
    public void CategoryAmbiguous_ReturnsMessage()
        => ClarificationTemplates.BuildMessage([AmbiguityReason.CategoryAmbiguous])
            .Should().NotBeNull();

    [Fact]
    public void LanguageUnclear_ReturnsMessage()
        => ClarificationTemplates.BuildMessage([AmbiguityReason.LanguageUnclear])
            .Should().NotBeNull();

    [Fact]
    public void NeedsVisual_ReturnsMessage()
        => ClarificationTemplates.BuildMessage([AmbiguityReason.NeedsVisual])
            .Should().NotBeNull();

    [Fact]
    public void Priority_MissingLocation_BeatsAll()
    {
        var msg = ClarificationTemplates.BuildMessage(
            [AmbiguityReason.CategoryAmbiguous, AmbiguityReason.MissingLocation,
             AmbiguityReason.MissingSeverity]);
        msg.Should().Contain("hangi daire");
    }

    [Fact]
    public void Priority_CategoryAmbiguous_BeatsMissingSeverity()
    {
        var msg = ClarificationTemplates.BuildMessage(
            [AmbiguityReason.MissingSeverity, AmbiguityReason.CategoryAmbiguous]);
        msg.Should().Contain("açıklar mısınız");
    }

    [Fact]
    public void NonActionable_WithOtherReason_OtherReasonWins()
    {
        var msg = ClarificationTemplates.BuildMessage(
            [AmbiguityReason.NonActionable, AmbiguityReason.MissingSeverity]);
        msg.Should().NotBeNull();
    }
}
