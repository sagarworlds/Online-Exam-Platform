using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Delivers the password-reset message by e-mail through the platform's mail sender (NFR-6). The message carries a link that opens the
/// reset page with the code filled in, the same code as plain text for a mail program that does not open links, and the lifetime the
/// code really has, which the caller passes in rather than this class guessing it.
/// </summary>
public sealed class SmtpPasswordResetSender(
    IMailSender mailSender, IPasswordResetLinkBuilder links, ILogger<SmtpPasswordResetSender> logger) : IPasswordResetSender
{
    /// <summary>The subject line; it names the action so the message is not mistaken for a sign-in code.</summary>
    public const string Subject = "Choose a new password for your exam platform account";

    /// <inheritdoc />
    public async Task SendAsync(string email, string code, TimeSpan validity, CancellationToken cancellationToken)
    {
        // The body is the only place the code and the link appear; neither this class nor the mail sender logs it.
        var sent = await mailSender.SendAsync(new OutgoingMail(email, Subject, Body(links.Build(code), code, validity)), cancellationToken);

        if (!sent)
        {
            // IMailSender has already logged why (not configured, refused, or unreachable). The caller still answers every request the
            // same way, so a throw here would reveal which addresses have accounts.
            logger.LogError(
                "The password-reset e-mail for {MaskedDestination} could not be delivered.", ContactMasker.Mask(OtpChannel.Email, email));
        }
    }

    private static string Body(string link, string code, TimeSpan validity)
    {
        // The page address without the code, for the person who has to paste the code by hand.
        var fragment = link.IndexOf('#', StringComparison.Ordinal);
        var page = fragment < 0 ? link : link[..fragment];

        return
            "A request was made to set or reset the password of your exam platform account.\r\n\r\n" +
            $"Open this link to choose a new password:\r\n\r\n{link}\r\n\r\n" +
            $"The link works once and expires in {Minutes(validity)}. If it does not open, go to {page} and paste this code " +
            $"into the reset code box:\r\n\r\n{code}\r\n\r\n" +
            "If you did not ask for this, you can ignore this message. Your password stays the same.";
    }

    // Rounded up, so the message never promises more time than there is left.
    private static string Minutes(TimeSpan validity)
    {
        var minutes = (int)Math.Ceiling(validity.TotalMinutes);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
