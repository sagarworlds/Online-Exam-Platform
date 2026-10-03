using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;
using ExamPlatform.SharedKernel.Infrastructure.Email;

namespace ExamPlatform.IntegrationTests;

/// <summary>What the answer to an attempt request says and who it goes to; how mail is delivered is the mail sender's job (see SmtpMailSenderTests).</summary>
public sealed class SmtpAttemptRequestNotifierTests
{
    private static readonly AttemptRequestDecisionEmail Approved = new("candidate@example.com", "Maths Final", Approved: true, Note: null);
    private static readonly AttemptRequestDecisionEmail Declined = new("candidate@example.com", "Maths Final", Approved: false, Note: "Speak to your teacher");

    private readonly RecordingMailSender _sender = new();

    private SmtpAttemptRequestNotifier Notifier => new(_sender);

    private async Task<OutgoingMail> SentFor(AttemptRequestDecisionEmail email)
    {
        Assert.True(await Notifier.SendDecisionAsync(email, CancellationToken.None));

        return Assert.Single(_sender.Sent);
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
        _sender.Delivers = delivered;

        Assert.Equal(delivered, await Notifier.SendDecisionAsync(Approved, CancellationToken.None));
    }

    [Fact]
    public async Task ANewRequest_GoesToTheStaffMember_NamingTheCandidateTheExamAndTheReason()
    {
        var sent = await Notifier.SendNewRequestAsync(
            new NewAttemptRequestEmail("admin@example.com", "Maths Final", "student@example.com", "Power cut"), CancellationToken.None);

        Assert.True(sent);
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("admin@example.com", mail.To);
        Assert.Equal("Request for another attempt at Maths Final", mail.Subject);
        Assert.Contains("student@example.com", mail.Body);
        Assert.Contains("Maths Final", mail.Body);
        Assert.Contains("Power cut", mail.Body);
        Assert.Contains("Attempt requests", mail.Body);
    }

    [Fact]
    public async Task ANewRequestWithNoReasonOrKnownCandidate_SaysSoRatherThanLeavingAGap()
    {
        await Notifier.SendNewRequestAsync(new NewAttemptRequestEmail("admin@example.com", "Maths Final", null, null), CancellationToken.None);

        var mail = Assert.Single(_sender.Sent);
        Assert.StartsWith("A candidate has asked", mail.Body);
        Assert.Contains("They gave no reason.", mail.Body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ANewRequestReportsWhatTheMailSenderReported(bool delivered)
    {
        _sender.Delivers = delivered;

        Assert.Equal(delivered, await Notifier.SendNewRequestAsync(new NewAttemptRequestEmail("admin@example.com", "Maths Final", null, null), CancellationToken.None));
    }
}
