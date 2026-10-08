using System.Globalization;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure.Email;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;

/// <summary>
/// Writes the platform's scheduled e-mails (FR-39) and hands them to the platform's mail sender. A failure to send is reported as
/// "not sent", never thrown: the run records the try and goes on, and tries the message again later.
/// <para>
/// Plain text, in English, with no link: the candidate signs in as usual and finds everything on "My exams", as with the other
/// e-mails. No message carries a score or anything else a stranger could use, except a revision, which says what changed and why.
/// </para>
/// </summary>
public sealed class SmtpExamNotifier(IMailSender mailSender) : IExamNotificationMailer
{
    // Candidates are in India, where there is no daylight saving, so a fixed offset is all that is needed to also show the local time.
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    /// <inheritdoc />
    public bool IsAvailable => mailSender.IsConfigured;

    /// <inheritdoc />
    public Task<bool> SendReminderAsync(ExamReminderEmail email, CancellationToken cancellationToken)
    {
        var when = email.WithinAnHour ? "in less than an hour" : "within a day";
        return mailSender.SendAsync(
            new OutgoingMail(
                email.To,
                email.WithinAnHour ? $"{email.ExamName} starts in less than an hour" : $"Reminder: {email.ExamName} starts {WhenShort(email.StartUtc)}",
                $"Your exam \"{email.ExamName}\" starts {when}.\r\n\r\n" +
                $"Starts: {At(email.StartUtc)}\r\n\r\n" +
                "Sign in and open My exams to read the instructions and check your connection before it starts."),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> SendResultReleasedAsync(ResultReleasedEmail email, CancellationToken cancellationToken) =>
        mailSender.SendAsync(
            new OutgoingMail(
                email.To,
                $"Your result for {email.ExamName} is ready",
                $"Your result for \"{email.ExamName}\"" + (email.AttemptNumber > 1 ? $" (attempt {email.AttemptNumber})" : string.Empty) + " can now be seen.\r\n\r\n" +
                "Sign in and open My exams to see your score and which answers were right."),
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> SendScoreRevisedAsync(ScoreRevisedEmail email, CancellationToken cancellationToken) =>
        mailSender.SendAsync(
            new OutgoingMail(
                email.To,
                $"Your score for {email.ExamName} was revised",
                $"Your score for \"{email.ExamName}\" changed from {Score(email.PreviousScore, email.PreviousMaxScore)} to {Score(email.NewScore, email.NewMaxScore)}.\r\n\r\n" +
                $"Why: {email.Reason}\r\n\r\n" +
                "Sign in and open My exams to see the new result."),
            cancellationToken);

    private static string Score(decimal score, decimal max) => string.Create(CultureInfo.InvariantCulture, $"{score:0.##} out of {max:0.##}");

    private static string WhenShort(DateTime startUtc) => string.Create(CultureInfo.InvariantCulture, $"on {startUtc:d MMM} at {startUtc:HH:mm} UTC");

    /// <summary>The start in UTC and in Indian Standard Time, so no candidate has to work one out from the other.</summary>
    private static string At(DateTime startUtc)
    {
        var utc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var india = utc + IndiaOffset;
        return string.Create(CultureInfo.InvariantCulture, $"{utc:d MMMM yyyy}, {utc:HH:mm} UTC ({india:d MMMM yyyy}, {india:HH:mm} IST)");
    }
}
