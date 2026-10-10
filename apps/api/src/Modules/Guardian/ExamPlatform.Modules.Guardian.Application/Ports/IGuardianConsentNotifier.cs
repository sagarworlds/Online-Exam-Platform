namespace ExamPlatform.Modules.Guardian.Application.Ports;

/// <summary>An e-mail asking a guardian to confirm that they are the guardian of a candidate (FR-39).</summary>
/// <param name="GuardianEmail">Where the request goes.</param>
/// <param name="GuardianName">The guardian's name, for the greeting.</param>
/// <param name="CandidateEmail">The candidate the guardian is asked to confirm.</param>
/// <param name="ConfirmLink">The page that carries the one-time code. It is a credential, so it appears only in the message.</param>
/// <param name="ExpiresAtUtc">When the code stops working.</param>
public sealed record GuardianConsentRequest(
    string GuardianEmail,
    string GuardianName,
    string CandidateEmail,
    string ConfirmLink,
    DateTime ExpiresAtUtc);

/// <summary>Delivers guardian confirmation requests. Real delivery is optional: with no mail server configured, nothing is sent.</summary>
public interface IGuardianConsentNotifier
{
    /// <summary>Tries to deliver the request.</summary>
    /// <param name="request">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the message was handed to a mail server; <see langword="false"/> when nothing was sent, either
    /// because e-mail is not configured or because the mail server refused it. The caller then hands the link to staff instead.
    /// </returns>
    Task<bool> SendAsync(GuardianConsentRequest request, CancellationToken cancellationToken);
}
