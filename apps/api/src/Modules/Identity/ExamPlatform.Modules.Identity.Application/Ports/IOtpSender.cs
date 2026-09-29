using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>
/// Delivers an OTP code to a candidate. The production implementation is an
/// email/SMS provider adapter (Notifications module, once it exists); this
/// module only depends on the abstraction (DIP), so swapping providers never
/// touches the login flow.
/// </summary>
public interface IOtpSender
{
    /// <summary>Sends a code to a destination over the given channel.</summary>
    /// <param name="channel">Email or SMS.</param>
    /// <param name="destination">The email address or phone number.</param>
    /// <param name="code">The plaintext code to deliver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken);
}
