namespace ExamPlatform.SharedKernel.Infrastructure.Email;

/// <summary>A plain-text message to be sent.</summary>
/// <param name="To">The recipient's address.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Body">The plain-text body. It may carry a credential (an invitation link), so a sender never logs it.</param>
public sealed record OutgoingMail(string To, string Subject, string Body);

/// <summary>
/// Hands a message to the platform's mail server. The one place that knows how mail is delivered, so each module's notifier only
/// decides what to say. Real delivery is optional: with no SMTP server configured nothing is sent.
/// </summary>
public interface IMailSender
{
    /// <summary>
    /// Whether there is a mail server to hand mail to at all. A job that sends mail on a schedule (FR-39 reminders) asks first, so with
    /// none configured it waits quietly and sends what is due once one is, instead of failing every message every minute. A sender
    /// that cannot tell says yes, and the answer to <see cref="SendAsync"/> is then the only signal.
    /// </summary>
    bool IsConfigured => true;

    /// <summary>Tries to deliver the message.</summary>
    /// <param name="mail">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the message was handed to a mail server; <see langword="false"/> when nothing was sent, either
    /// because e-mail is not configured or because the mail server refused it or could not be reached. It does not throw for either:
    /// the caller has already saved whatever the message is about, and tells the person to pass it on by other means.
    /// </returns>
    Task<bool> SendAsync(OutgoingMail mail, CancellationToken cancellationToken);
}
