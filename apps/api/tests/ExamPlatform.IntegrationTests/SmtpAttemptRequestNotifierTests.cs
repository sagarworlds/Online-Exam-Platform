using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using NSubstitute;

namespace ExamPlatform.IntegrationTests;

/// <summary>What the answer to an attempt request says and who it goes to; how mail is delivered is the mail sender's job (see SmtpMailSenderTests).</summary>
public sealed class SmtpAttemptRequestNotifierTests
{
    private static readonly AttemptRequestDecisionEmail Approved = new("candidate@example.com", "Maths Final", Approved: true, Note: null);
    private static readonly AttemptRequestDecisionEmail Declined = new("candidate@example.com", "Maths Final", Approved: false, Note: "Speak to your teacher");

    private readonly IMailSender _sender = Substitute.For<IMailSender>();

    private SmtpAttemptRequestNotifier Notifier => new(_sender);

    private async Task<OutgoingMail> SentFor(AttemptRequestDecisionEmail email)
    {
        OutgoingMail? sent = null;
        _sender.SendAsync(Arg.Do<OutgoingMail>(m => sent = m), Arg.Any<CancellationToken>()).Returns(true);

        Assert.True(await Notifier.SendDecisionAsync(email, CancellationToken.None));

        return sent ?? throw new InvalidOperationException("Nothing was handed to the mail sender.");
    }

    [Fact]
    public async Task AnApproval_GoesToTheCandidate_AndSaysTheyCanSitAgain()
    {
        var mail = await SentFor(Approved);

        Assert.Equal("candidate@example.com", mail.To);
        Assert.Equal("You can take Maths Final again", mail.Subject);
        Assert.Contains("another attempt", mail.Body);
    }

    [Fact]
    public async Task ADecline_CarriesTheAdministratorsReason()
    {
        var mail = await SentFor(Declined);

        Assert.Equal("Your request for another attempt at Maths Final", mail.Subject);
        Assert.Contains("was declined", mail.Body);
        Assert.Contains("Speak to your teacher", mail.Body);
    }

    [Fact]
    public async Task ADeclineWithoutAReason_DoesNotInventOne()
    {
        var mail = await SentFor(Declined with { Note = null });

        Assert.DoesNotContain("What they said", mail.Body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ItReportsWhatTheMailSenderReported_SoTheAdministratorKnowsWhetherTheyWereTold(bool delivered)
    {
        _sender.SendAsync(Arg.Any<OutgoingMail>(), Arg.Any<CancellationToken>()).Returns(delivered);

        Assert.Equal(delivered, await Notifier.SendDecisionAsync(Approved, CancellationToken.None));
    }
}
