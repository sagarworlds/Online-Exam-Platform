namespace ExamPlatform.Modules.Invite.Application.Ports;

/// <summary>An invitation e-mail to be sent.</summary>
/// <param name="To">The invited address.</param>
/// <param name="ExamName">The exam's name, for the subject and body.</param>
/// <param name="Link">The link that carries the invite code.</param>
/// <param name="ExpiresAtUtc">When the link stops working.</param>
public sealed record InviteEmail(string To, string ExamName, string Link, DateTime ExpiresAtUtc);

/// <summary>Delivers invitation e-mails (FR-14, FR-39). Real delivery is optional: with no SMTP configured nothing is sent.</summary>
public interface IInviteNotifier
{
    /// <summary>Tries to deliver the invitation.</summary>
    /// <param name="email">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the message was handed to a mail server; <see langword="false"/> when nothing was sent,
    /// either because e-mail is not configured or because the mail server refused it. The caller then gives the
    /// inviter the link to pass on by other means.
    /// </returns>
    Task<bool> SendAsync(InviteEmail email, CancellationToken cancellationToken);
}

/// <summary>Builds the link a candidate opens to accept an invitation.</summary>
public interface IInviteLinkBuilder
{
    /// <summary>The web link for a code.</summary>
    /// <param name="code">The invite code.</param>
    string Build(string code);
}
