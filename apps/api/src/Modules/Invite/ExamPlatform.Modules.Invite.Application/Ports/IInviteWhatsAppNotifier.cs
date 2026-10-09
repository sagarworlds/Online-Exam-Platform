namespace ExamPlatform.Modules.Invite.Application.Ports;

/// <summary>An invitation to be sent to a phone on WhatsApp, with the code to type on the invitation page.</summary>
/// <param name="PhoneNumber">The invited account holder's number, as registered.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="Code">The invite code, the exam code the candidate enters on the invitation page.</param>
/// <param name="Link">The same code as a link.</param>
/// <param name="ExpiresAtUtc">When the code stops working.</param>
public sealed record InviteWhatsAppMessage(string PhoneNumber, string ExamName, string Code, string Link, DateTime ExpiresAtUtc);

/// <summary>Delivers an invitation's code to the invited person's phone on WhatsApp, in addition to the e-mail (FR-14, FR-39).</summary>
public interface IInviteWhatsAppNotifier
{
    /// <summary>
    /// Whether this host sends invitations on WhatsApp at all. It is off until the operator configures a template, so a caller checks
    /// it before looking a phone number up: nobody's number is read for a message that will not be sent.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Tries to deliver the invitation.</summary>
    /// <param name="message">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when WhatsApp accepted the message; <see langword="false"/> when nothing was sent, because it is not
    /// configured, the number cannot be reached or WhatsApp refused it. Acceptance is not delivery.
    /// </returns>
    Task<bool> SendAsync(InviteWhatsAppMessage message, CancellationToken cancellationToken);
}
