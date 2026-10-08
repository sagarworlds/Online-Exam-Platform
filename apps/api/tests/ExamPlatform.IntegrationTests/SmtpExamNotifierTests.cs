using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;
using ExamPlatform.SharedKernel.Infrastructure.Email;

namespace ExamPlatform.IntegrationTests;

/// <summary>What the platform's scheduled e-mails say and who they go to (FR-39); how mail is delivered is the mail sender's job (see SmtpMailSenderTests).</summary>
public sealed class SmtpExamNotifierTests
{
    private readonly RecordingMailSender _sender = new();

    private SmtpExamNotifier Notifier => new(_sender);

    private OutgoingMail OnlyMail => Assert.Single(_sender.Sent);

    [Fact]
    public void ItIsAvailableExactlyWhenTheMailSenderIsConfigured()
    {
        _sender.IsConfigured = false;
        Assert.False(Notifier.IsAvailable);

        _sender.IsConfigured = true;
        Assert.True(Notifier.IsAvailable);
    }

    [Fact]
    public async Task ADayReminder_GoesToTheCandidate_AndSaysWhenTheExamStartsInUtcAndInIndia()
    {
        var start = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

        Assert.True(await Notifier.SendReminderAsync(new ExamReminderEmail("candidate@example.com", "Maths Final", start, WithinAnHour: false), CancellationToken.None));

        Assert.Equal("candidate@example.com", OnlyMail.To);
        Assert.Equal("Reminder: Maths Final starts on 12 Oct at 09:00 UTC", OnlyMail.Subject);
        Assert.Contains("\"Maths Final\" starts within a day", OnlyMail.Body);
        Assert.Contains("12 October 2026, 09:00 UTC (12 October 2026, 14:30 IST)", OnlyMail.Body);
        Assert.Contains("My exams", OnlyMail.Body);
    }

    [Fact]
    public async Task AnHourReminder_SaysSoInTheSubject()
    {
        var start = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);

        await Notifier.SendReminderAsync(new ExamReminderEmail("candidate@example.com", "Maths Final", start, WithinAnHour: true), CancellationToken.None);

        Assert.Equal("Maths Final starts in less than an hour", OnlyMail.Subject);
        Assert.Contains("starts in less than an hour", OnlyMail.Body);
        // Past midnight in India: the local date moves on and the message says so.
        Assert.Contains("12 October 2026, 20:00 UTC (13 October 2026, 01:30 IST)", OnlyMail.Body);
    }

    [Fact]
    public async Task AReleasedResult_SaysWhereToLook_WithoutGivingTheScore()
    {
        await Notifier.SendResultReleasedAsync(new ResultReleasedEmail("candidate@example.com", "Maths Final", AttemptNumber: 1), CancellationToken.None);

        Assert.Equal("Your result for Maths Final is ready", OnlyMail.Subject);
        Assert.Contains("can now be seen", OnlyMail.Body);
        Assert.DoesNotContain("attempt", OnlyMail.Body);
        Assert.Contains("My exams", OnlyMail.Body);
    }

    [Fact]
    public async Task AReleasedResultOfALaterAttempt_NamesTheAttempt()
    {
        await Notifier.SendResultReleasedAsync(new ResultReleasedEmail("candidate@example.com", "Maths Final", AttemptNumber: 2), CancellationToken.None);

        Assert.Contains("(attempt 2)", OnlyMail.Body);
    }

    [Fact]
    public async Task ARevisedScore_SaysWhatItWasWhatItIsAndWhy()
    {
        await Notifier.SendScoreRevisedAsync(
            new ScoreRevisedEmail("candidate@example.com", "Maths Final", 4m, 12m, 5.5m, 12m, "The key of question 3 was wrong"), CancellationToken.None);

        Assert.Equal("candidate@example.com", OnlyMail.To);
        Assert.Equal("Your score for Maths Final was revised", OnlyMail.Subject);
        Assert.Contains("changed from 4 out of 12 to 5.5 out of 12", OnlyMail.Body);
        Assert.Contains("Why: The key of question 3 was wrong", OnlyMail.Body);
    }

    [Fact]
    public async Task WhenTheMailServerRefuses_TheAnswerIsFalse_NotAnException()
    {
        _sender.Delivers = false;

        Assert.False(await Notifier.SendReminderAsync(new ExamReminderEmail("candidate@example.com", "Maths Final", DateTime.UtcNow, WithinAnHour: false), CancellationToken.None));
        Assert.False(await Notifier.SendResultReleasedAsync(new ResultReleasedEmail("candidate@example.com", "Maths Final", 1), CancellationToken.None));
        Assert.False(await Notifier.SendScoreRevisedAsync(new ScoreRevisedEmail("candidate@example.com", "Maths Final", 1m, 2m, 2m, 2m, "Why"), CancellationToken.None));
    }
}
