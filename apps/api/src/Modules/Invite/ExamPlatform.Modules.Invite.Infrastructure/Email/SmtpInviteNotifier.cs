using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure.Email;

namespace ExamPlatform.Modules.Invite.Infrastructure.Email;

/// <summary>
/// Writes the invitation and hands it to the platform's mail sender. With no mail server configured nothing is sent, so the inviter
/// is handed the link instead (FR-14, FR-39). The link is a credential, so it appears only in the message itself.
/// </summary>
public sealed class SmtpInviteNotifier(IMailSender mailSender) : IInviteNotifier
{
    /// <inheritdoc />
    public Task<bool> SendAsync(InviteEmail email, CancellationToken cancellationToken) =>
        mailSender.SendAsync(
            new OutgoingMail(email.To, $"You are invited to take {email.ExamName}", Body(email)),
            cancellationToken);

    private static string Body(InviteEmail email) => email.DirectSignIn
        // A direct link signs in whoever opens it, so the message says it is personal; nothing in it is typed in.
        ? $"You have been invited to take the exam \"{email.ExamName}\".\r\n\r\n" +
          $"Open this link to go straight to your exam. You are signed in by the link, so there is no code to enter:\r\n{email.Link}\r\n\r\n" +
          $"The link works once and expires at {email.ExpiresAtUtc:u}. It is meant for you only, so please do not forward it."
        : $"You have been invited to take the exam \"{email.ExamName}\".\r\n\r\n" +
          $"Open this link, sign in with this e-mail address, and accept the invitation:\r\n{email.Link}\r\n\r\n" +
          $"The link works once and expires at {email.ExpiresAtUtc:u}.";
}
