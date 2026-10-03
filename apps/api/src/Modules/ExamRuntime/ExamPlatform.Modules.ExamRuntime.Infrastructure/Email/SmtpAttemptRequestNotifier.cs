using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure.Email;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;

/// <summary>
/// Writes the answer to an attempt request and hands it to the platform's mail sender. A failure to send is reported as "not sent",
/// never thrown: the decision is already saved, and losing it over a mail outage would be worse.
/// </summary>
public sealed class SmtpAttemptRequestNotifier(IMailSender mailSender) : IAttemptRequestNotifier
{
    /// <inheritdoc />
    public Task<bool> SendDecisionAsync(AttemptRequestDecisionEmail email, CancellationToken cancellationToken) =>
        mailSender.SendAsync(
            new OutgoingMail(
                email.To,
                email.Approved ? $"You can take {email.ExamName} again" : $"Your request for another attempt at {email.ExamName}",
                BodyFor(email)),
            cancellationToken);

    // Plain text, and no link: the candidate signs in as usual and finds the answer on "My exams".
    private static string BodyFor(AttemptRequestDecisionEmail email)
    {
        if (email.Approved)
        {
            return $"An administrator has given you another attempt at \"{email.ExamName}\".\r\n\r\n" +
                   "Sign in and open My exams to start it while the exam is still open.";
        }

        var reason = string.IsNullOrWhiteSpace(email.Note) ? string.Empty : $"\r\n\r\nWhat they said: {email.Note}";
        return $"Your request for another attempt at \"{email.ExamName}\" was declined.{reason}\r\n\r\n" +
               "You can see this on My exams, and ask again there if you need to.";
    }
}
