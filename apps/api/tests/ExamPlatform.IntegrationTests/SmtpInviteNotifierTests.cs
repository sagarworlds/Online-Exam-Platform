using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Infrastructure.Email;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using NSubstitute;

namespace ExamPlatform.IntegrationTests;

/// <summary>What an invitation says and who it goes to; how mail is delivered is the mail sender's job (see SmtpMailSenderTests).</summary>
public sealed class SmtpInviteNotifierTests
{
    private static readonly InviteEmail Email =
        new("candidate@example.com", "Maths Final", "https://app.example/invite?code=AB12CD34", new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc));

    private readonly IMailSender _sender = Substitute.For<IMailSender>();

    private SmtpInviteNotifier Notifier => new(_sender);

    [Fact]
    public async Task Send_AddressesTheInvitedPerson_NamesTheExam_AndCarriesTheLinkAndItsExpiry()
    {
        OutgoingMail? sent = null;
        _sender.SendAsync(Arg.Do<OutgoingMail>(m => sent = m), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Notifier.SendAsync(Email, CancellationToken.None);

        Assert.True(result);
        Assert.NotNull(sent);
        Assert.Equal("candidate@example.com", sent.To);
        Assert.Equal("You are invited to take Maths Final", sent.Subject);
        Assert.Contains("AB12CD34", sent.Body);
        Assert.Contains("2026-10-05", sent.Body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_ReportsWhatTheMailSenderReported_SoTheInviterCanPassTheLinkOnByHand(bool delivered)
    {
        _sender.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(delivered);

        Assert.Equal(delivered, await Notifier.SendAsync(Email, CancellationToken.None));
    }
}
