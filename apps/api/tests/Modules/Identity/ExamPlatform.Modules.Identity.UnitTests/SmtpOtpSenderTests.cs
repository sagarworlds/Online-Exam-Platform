using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class SmtpOtpSenderTests
{
    private readonly IMailSender _mail = Substitute.For<IMailSender>();
    private readonly ILogger<SmtpOtpSender> _logger = Substitute.For<ILogger<SmtpOtpSender>>();

    [Fact]
    public async Task SendAsync_ByEmail_MailsTheCodeToTheDestination()
    {
        _mail.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(true);

        await new SmtpOtpSender(_mail, _logger).SendAsync(OtpChannel.Email, "amy@example.com", "123456", CancellationToken.None);

        await _mail.Received(1).SendAsync(
            Arg.Is<OutgoingMail>(m => m.To == "amy@example.com" && m.Body.Contains("123456")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_BySms_SendsNothingAndDoesNotThrow()
    {
        await new SmtpOtpSender(_mail, _logger).SendAsync(OtpChannel.Sms, "+15550100", "123456", CancellationToken.None);

        await _mail.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task SendAsync_WhenTheMailServerRefuses_DoesNotThrow()
    {
        _mail.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(false);

        var send = new SmtpOtpSender(_mail, _logger).SendAsync(OtpChannel.Email, "amy@example.com", "123456", CancellationToken.None);

        // The request is answered the same either way, so a failure is logged, not surfaced as an error.
        await send;
    }
}
