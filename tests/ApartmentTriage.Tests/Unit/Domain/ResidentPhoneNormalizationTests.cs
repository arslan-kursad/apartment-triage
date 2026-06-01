using ApartmentTriage.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace ApartmentTriage.Tests.Unit.Domain;

public class ResidentPhoneNormalizationTests
{
    [Theory]
    [InlineData("905550001234")]
    [InlineData("+905550001234")]
    public void Create_StoresCanonicalWhatsApp(string input)
    {
        var resident = Resident.Create(whatsAppNumber: input);
        resident.WhatsAppNumber.Should().Be("+905550001234");
    }

    [Fact]
    public void UpdateContactInfo_NormalizesContactPhone()
    {
        var resident = Resident.Create();
        resident.UpdateContactInfo(contactPhone: "05550001234");
        resident.ContactPhone.Should().Be("+905550001234");
    }
}
