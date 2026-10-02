using System.Net;
using System.Net.Mail;
using ExamPlatform.Modules.Invite.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Invite.Infrastructure.Email;

/// <summary>
/// Sends invitations through the configured SMTP server. With no server configured it sends nothing and says so,
/// so the inviter is handed the link instead (FR-14, FR-39). It never writes the link to a log: the link is a credential.
/// </summary>
public sealed class SmtpInviteNotifier(IOptions<SmtpOptions> options, ILogger<SmtpInviteNotifier> logger) : IInviteNotifier
{
    /// <inheritdoc />
    public async Task<bool> SendAsync(InviteEmail email, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        if (!smtp.IsConfigured)
        {
            logger.LogWarning("Invitation e-mail not sent: no SMTP server is configured (Smtp:Host).");
            return false;
        }

        using var message = new MailMessage(smtp.From, email.To)
        {
            Subject = $"You are invited to take {email.ExamName}",
            Body =
                $"You have been invited to take the exam \"{email.ExamName}\".\r\n\r\n" +
                $"Open this link, sign in with this e-mail address, and accept the invitation:\r\n{email.Link}\r\n\r\n" +
                $"The link works once and expires at {email.ExpiresAtUtc:u}.",
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
            // Reported as "not sent", not thrown: the invite is already stored and the inviter can pass the link on.
            logger.LogError(ex, "The mail server refused the invitation e-mail (status {StatusCode}).", ex.StatusCode);
            return false;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "The connection to the mail server failed while sending an invitation e-mail.");
            return false;
        }
    }
}
