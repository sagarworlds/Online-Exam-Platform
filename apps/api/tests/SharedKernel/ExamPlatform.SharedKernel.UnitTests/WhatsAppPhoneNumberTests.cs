using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

namespace ExamPlatform.SharedKernel.UnitTests;

public class WhatsAppPhoneNumberTests
{
    [Theory]
    [InlineData("+919876543210", "91", "919876543210")]
    [InlineData("+91 98765-43210", "91", "919876543210")]
    [InlineData("+91 (98765) 43.210", "91", "919876543210")]
    [InlineData("9876543210", "91", "919876543210")]
    [InlineData("098765 43210", "91", "919876543210")]
    [InlineData("919876543210", "91", "919876543210")]
    [InlineData("00919876543210", "91", "919876543210")]
    [InlineData("  9876543210  ", "+91", "919876543210")]
    [InlineData("+1 (555) 010-0100", "91", "15550100100")]
    [InlineData("0015550100100", "91", "15550100100")]
    public void Normalize_GivesTheCountryCodeAndNumberAsDigits(string raw, string? country, string expected) =>
        Assert.Equal(expected, WhatsAppPhoneNumber.Normalize(raw, country));

    [Theory]
    [InlineData("9876543210")]
    [InlineData("919876543210")]
    public void Normalize_WithNoDefaultCountryCode_AddsNothing(string raw)
    {
        Assert.Equal(raw, WhatsAppPhoneNumber.Normalize(raw, null));
        Assert.Equal(raw, WhatsAppPhoneNumber.Normalize(raw, "  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("98765x43210")]
    [InlineData("9876+543210")]
    [InlineData("+91")]
    [InlineData("+9198")]
    [InlineData("12345")]
    [InlineData("+9198765432101234")]
    [InlineData("amy@example.com")]
    public void Normalize_RefusesWhatIsNotAPhoneNumber(string? raw) =>
        Assert.Null(WhatsAppPhoneNumber.Normalize(raw, "91"));

    [Theory]
    [InlineData("919876543210", "**********10")]
    [InlineData("1234", "****")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    public void Mask_HidesAllButTheLastTwoDigits(string? number, string expected) =>
        Assert.Equal(expected, WhatsAppPhoneNumber.Mask(number));
}
