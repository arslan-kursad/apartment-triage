using ApartmentTriage.Domain;
using FluentAssertions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Domain;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("+905550001234", "+905550001234")]
    [InlineData("905550001234", "+905550001234")]
    [InlineData("5550001234", "+905550001234")]
    [InlineData("05550001234", "+905550001234")]
    [InlineData("+90 555 000 12 34", "+905550001234")]
    [InlineData("90-555-000-12-34", "+905550001234")]
    public void Normalize_TurkishMobile_ReturnsCanonicalE164(string input, string expected)
    {
        PhoneNumberNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("+441234567890")]
    public void Normalize_Invalid_ReturnsNull(string? input)
    {
        PhoneNumberNormalizer.Normalize(input).Should().BeNull();
    }

    [Fact]
    public void Normalize_Masked_PassesThrough()
    {
        PhoneNumberNormalizer.Normalize("+90***5993").Should().Be("+90***5993");
    }

    [Fact]
    public void ToWhatsAppApiRecipient_StripsPlus()
    {
        PhoneNumberNormalizer.ToWhatsAppApiRecipient("+905550001234")
            .Should().Be("905550001234");
    }
}
