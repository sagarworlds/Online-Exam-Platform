using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class WhatsAppOtpSenderTests
{
    private readonly IWhatsAppSender _whatsApp = Substitute.For<IWhatsAppSender>();
    private readonly ILogger<WhatsAppOtpSender> _logger = Substitute.For<ILogger<WhatsAppOtpSender>>();

    private WhatsAppOtpSender Sender(WhatsAppOptions? options = null) =>
        new(
            _whatsApp,
            Options.Create(options ?? new WhatsAppOptions { OtpTemplateName = "exam_login_code", OtpTemplateLanguage = "en", DefaultCountryCode = "91" }),
            _logger);

    [Fact]
    public async Task SendAsync_ToAPhone_SendsTheCodeTemplateToTheNumberWithItsCountryCode()
    {
        _whatsApp.SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WhatsAppSendResult(true, "wamid.1"));

        await Sender().SendAsync(OtpChannel.Sms, "98765 43210", "123456", CancellationToken.None);

        await _whatsApp.Received(1).SendTemplateAsync(
            Arg.Is<WhatsAppTemplateMessage>(m =>
                m.To == "919876543210"
                && m.TemplateName == "exam_login_code"
                && m.LanguageCode == "en"
                && m.BodyParameters.SequenceEqual(new[] { "123456" })
                && m.UrlButtonParameter == "123456"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_UsesTheTemplateLanguageAndCountryCodeFromTheSettings()
    {
        _whatsApp.SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WhatsAppSendResult(true, "wamid.1"));
        var options = new WhatsAppOptions { OtpTemplateName = "kod", OtpTemplateLanguage = "hi", DefaultCountryCode = "44" };

        await Sender(options).SendAsync(OtpChannel.Sms, "07911 123456", "654321", CancellationToken.None);

        await _whatsApp.Received(1).SendTemplateAsync(
            Arg.Is<WhatsAppTemplateMessage>(m => m.To == "447911123456" && m.TemplateName == "kod" && m.LanguageCode == "hi"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ToAnEmailAddress_SendsNothing()
    {
        await Sender().SendAsync(OtpChannel.Email, "amy@example.com", "123456", CancellationToken.None);

        await _whatsApp.DidNotReceiveWithAnyArgs().SendTemplateAsync(default!, default);
    }

    [Theory]
    [InlineData("not a number")]
    [InlineData("12345")]
    [InlineData("+9198")]
    public async Task SendAsync_ToSomethingThatIsNotAPhoneNumber_SendsNothingAndDoesNotThrow(string destination)
    {
        // The request is answered the same either way, so a number WhatsApp cannot reach is logged, not surfaced.
        await Sender().SendAsync(OtpChannel.Sms, destination, "123456", CancellationToken.None);

        await _whatsApp.DidNotReceiveWithAnyArgs().SendTemplateAsync(default!, default);
    }

    [Fact]
    public async Task SendAsync_WhenWhatsAppDoesNotAcceptIt_DoesNotThrow()
    {
        _whatsApp.SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WhatsAppSendResult(false));

        await Sender().SendAsync(OtpChannel.Sms, "9876543210", "123456", CancellationToken.None);

        await _whatsApp.Received(1).SendTemplateAsync(Arg.Any<WhatsAppTemplateMessage>(), Arg.Any<CancellationToken>());
    }
}
