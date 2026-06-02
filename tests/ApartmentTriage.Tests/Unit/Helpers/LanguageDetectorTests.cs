using ApartmentTriage.Web.Helpers;
using FluentAssertions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Helpers;

public class LanguageDetectorTests
{
    [Theory]
    // English messages (the regression: a Turkish-locale user writing English got Turkish replies)
    [InlineData("The hallway lights on 2nd floor aren't working", "en")]
    [InlineData("There's a gas smell near the stairwell", "en")]
    [InlineData("water leak in my apartment", "en")]
    // Turkish via special characters
    [InlineData("Asansör 3. katta arızalı, çalışmıyor", "tr")]
    [InlineData("Benim daire de böyle bir sorun var", "tr")]
    // Turkish without special characters (keyword-scored)
    [InlineData("Sular kesik", "tr")]
    [InlineData("Hala sorun var", "tr")]
    public void Detect_FromContent(string text, string expected)
        => LanguageDetector.Detect(text).Should().Be(expected);

    [Fact]
    public void Detect_EnglishContent_BeatsTurkishAppLanguage()
    {
        // languageCode "tr" (Telegram app locale) must NOT override clear English content.
        LanguageDetector.Detect("The lights are not working", "tr").Should().Be("en");
    }

    [Fact]
    public void Detect_NoSignal_FallsBackToChannelLanguage()
    {
        LanguageDetector.Detect("12345", "tr").Should().Be("tr");
        LanguageDetector.Detect("12345", "en").Should().Be("en");
        LanguageDetector.Detect("12345", null).Should().Be("en");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Detect_Blank_UsesChannelLanguage(string? text)
        => LanguageDetector.Detect(text, "tr").Should().Be("tr");
}
