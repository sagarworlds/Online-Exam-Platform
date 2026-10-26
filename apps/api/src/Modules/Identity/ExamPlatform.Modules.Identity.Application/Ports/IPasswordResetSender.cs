namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>
/// Delivers a password-reset message. It is a port of its own, not <see cref="IOtpSender"/>, because a reset is not a one-time sign-in
/// code: it lives longer (see <c>RequestPasswordResetHandler</c>) and is opened as a link, so the wording and the lifetime it states
/// differ. The same message also sets the first administrator's password (<c>ProvisionFirstAdministratorHandler</c>).
/// </summary>
public interface IPasswordResetSender
{
    /// <summary>Sends the reset message to an account's e-mail address.</summary>
    /// <param name="email">The account's e-mail address.</param>
    /// <param name="code">
    /// The reset code, <c>{tokenId}:{secret}</c>. The message turns it into a link (<see cref="IPasswordResetLinkBuilder"/>) and also
    /// shows it as it is, for a person whose mail program does not open the link.
    /// </param>
    /// <param name="validity">How long the code works, so the message states the real limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(string email, string code, TimeSpan validity, CancellationToken cancellationToken);
}
