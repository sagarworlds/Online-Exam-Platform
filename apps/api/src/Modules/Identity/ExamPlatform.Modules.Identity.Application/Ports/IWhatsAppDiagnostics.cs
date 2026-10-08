using ExamPlatform.Modules.Identity.Application.Dtos;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>What an administrator's test sends: a message they write, or the sign-in code template.</summary>
public enum WhatsAppTestMode
{
    /// <summary>A plain text message the administrator wrote. WhatsApp delivers it only to someone who messaged the business number in the last 24 hours.</summary>
    Text,

    /// <summary>The approved sign-in code template with a test code, which works for anyone.</summary>
    SignInTemplate,
}

/// <summary>
/// Lets an administrator check that WhatsApp works: what is configured, a message sent through the real connection, and what became of
/// it. The Application layer asks and audits; an adapter talks to WhatsApp.
/// </summary>
public interface IWhatsAppDiagnostics
{
    /// <summary>Reviews the configuration without calling Meta.</summary>
    WhatsAppStatusDto GetStatus();

    /// <summary>
    /// Sends a test message through the real connection. Never throws for a problem sending: the reason comes back in the result.
    /// </summary>
    /// <param name="mode">What to send.</param>
    /// <param name="phoneNumber">The recipient, as typed; the platform's default country code is added when it has none.</param>
    /// <param name="message">The text, for <see cref="WhatsAppTestMode.Text"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WhatsAppSendResultDto> SendAsync(WhatsAppTestMode mode, string phoneNumber, string? message, CancellationToken cancellationToken);

    /// <summary>What Meta has reported about a message, or <c>NotReported</c> when it has not (yet).</summary>
    /// <param name="messageId">WhatsApp's id for the message.</param>
    WhatsAppDeliveryDto FindDelivery(string messageId);
}
