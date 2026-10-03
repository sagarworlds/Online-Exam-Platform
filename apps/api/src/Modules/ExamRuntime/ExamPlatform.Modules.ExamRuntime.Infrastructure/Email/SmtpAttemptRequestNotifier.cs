using System.Net;
using System.Net.Mail;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;

/// <summary>
/// Sends the answer to an attempt request through the configured SMTP server. With no server configured it sends nothing and says so.
/// A failure is reported as "not sent", never thrown: the decision is already saved, and losing it over a mail outage would be worse.
/// </summary>
public sealed class SmtpAttemptRequestNotifier(IOptions<SmtpOptions> options, ILogger<SmtpAttemptRequestNotifier> logger) : IAttemptRequestNotifier
{
    /// <inheritdoc />
    public async Task<bool> SendDecisionAsync(AttemptRequestDecisionEmail email, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        if (!smtp.IsConfigured)
        {
            logger.LogWarning("Attempt request e-mail not sent: no SMTP server is configured (Smtp:Host).");
            return false;
        }

        using var message = new MailMessage(smtp.From, email.To)
        {
            Subject = email.Approved
                ? $"You can take {email.ExamName} again"
                : $"Your request for another attempt at {email.ExamName}",
            Body = BodyFor(email),
        };

        using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.EnableSsl };
        if (!string.IsNullOrWhiteSpace(smtp.User))
            client.Credentials = new NetworkCredential(smtp.User, smtp.Password);

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "The mail server refused the attempt request e-mail (status {StatusCode}).", ex.StatusCode);
            return false;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "The connection to the mail server failed while sending an attempt request e-mail.");
            return false;
        }
    }

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
