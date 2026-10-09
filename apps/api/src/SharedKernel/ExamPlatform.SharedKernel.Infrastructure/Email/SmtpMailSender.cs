using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.Email;

/// <summary>
/// Sends mail through the configured SMTP server. With no server configured it sends nothing and says so. It never writes a message's
/// body or subject to a log: a body can carry a credential, such as an invitation link.
/// </summary>
public sealed class SmtpMailSender(IOptions<SmtpOptions> options, ILogger<SmtpMailSender> logger) : IMailSender
{
    /// <inheritdoc />
    public bool IsConfigured => options.Value.IsConfigured;

    /// <inheritdoc />
    public async Task<bool> SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        if (!smtp.IsConfigured)
        {
            logger.LogWarning("E-mail not sent: no SMTP server is configured (Smtp:Host).");
            return false;
        }

        using var message = new MailMessage(smtp.From, mail.To) { Subject = mail.Subject, Body = mail.Body };
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
            logger.LogError(ex, "The mail server refused a message (status {StatusCode}).", ex.StatusCode);
            return false;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "The connection to the mail server failed while sending a message.");
            return false;
        }
    }
}
