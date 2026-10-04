using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Delivers one-time codes (and password-reset codes) by e-mail through the platform's mail sender, so a deployed host
/// can sign people in without writing codes to a log (NFR-6). SMS is not supported yet (FR-39): an SMS request is reported
/// in the log and sends nothing, rather than being dropped without a trace.
/// </summary>
public sealed class SmtpOtpSender(IMailSender mailSender, ILogger<SmtpOtpSender> logger) : IOtpSender
{
    /// <inheritdoc />
    public async Task SendAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken)
    {
        var masked = ContactMasker.Mask(channel, destination);
        if (channel != OtpChannel.Email)
        {
            // The caller answers every request the same way, so a throw here would reveal which destinations have accounts.
            logger.LogError("A {Channel} code for {MaskedDestination} was not sent: only e-mail delivery is available.", channel, masked);
            return;
        }

        // The body is the only place the code appears; neither this class nor the mail sender logs it.
        var sent = await mailSender.SendAsync(
            new OutgoingMail(
                destination,
                "Your exam platform code",
                $"Your code is:\r\n\r\n{code}\r\n\r\nIt works once and expires in 10 minutes. " +
                "If you did not ask for it, you can ignore this message."),
            cancellationToken);

        if (!sent)
        {
            // IMailSender has already logged why (not configured, refused, or unreachable).
            logger.LogError("The e-mailed code for {MaskedDestination} could not be delivered.", masked);
        }
    }
}
