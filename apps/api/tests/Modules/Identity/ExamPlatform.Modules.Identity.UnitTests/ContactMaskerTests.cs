using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class ContactMaskerTests
{
    [Theory]
    [InlineData("jane.doe@example.com", "j***@example.com")]
    [InlineData("jo@school.edu.in", "j***@school.edu.in")]
    [InlineData("\"odd@local\"@example.com", "\"***@example.com")]
    public void Mask_Email_KeepsFirstCharAndDomain(string email, string expected) =>
        Assert.Equal(expected, ContactMasker.Mask(OtpChannel.Email, email));

    [Theory]
    [InlineData("+919876543210", "***********10")]
    [InlineData("9876543210", "********10")]
    [InlineData("12345", "***45")]
    public void Mask_Phone_KeepsLastTwoDigits(string phoneNumber, string expected) =>
        Assert.Equal(expected, ContactMasker.Mask(OtpChannel.Sms, phoneNumber));

    [Theory]
    [InlineData(OtpChannel.Email, "j@example.com", "***@example.com")]
    [InlineData(OtpChannel.Email, "@example.com", "***@example.com")]
    [InlineData(OtpChannel.Email, "not-an-email", "***")]
    [InlineData(OtpChannel.Email, "a", "***")]
    [InlineData(OtpChannel.Email, "", "***")]
    [InlineData(OtpChannel.Email, null, "***")]
    [InlineData(OtpChannel.Sms, "1234", "****")]
    [InlineData(OtpChannel.Sms, "12", "**")]
    [InlineData(OtpChannel.Sms, "7", "*")]
    [InlineData(OtpChannel.Sms, "", "***")]
    [InlineData(OtpChannel.Sms, null, "***")]
    public void Mask_ShortInputs_NeverRevealWholeValue(OtpChannel channel, string? destination, string expected)
    {
        var masked = ContactMasker.Mask(channel, destination);

        Assert.Equal(expected, masked);
        if (!string.IsNullOrEmpty(destination))
        {
            Assert.NotEqual(destination, masked);
        }
    }

    [Fact]
    public void Mask_UnknownChannel_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactMasker.Mask((OtpChannel)99, "jane@example.com"));
}
