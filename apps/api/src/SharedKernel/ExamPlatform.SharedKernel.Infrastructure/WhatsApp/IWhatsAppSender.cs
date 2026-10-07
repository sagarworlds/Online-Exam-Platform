namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// One approved message template, filled in for one recipient. WhatsApp lets a business start a conversation only with a
/// template Meta has approved, so everything a module sends this way (a one-time code now; invitations and reminders later)
/// is one of these.
/// </summary>
/// <param name="To">The recipient's number as <see cref="WhatsAppPhoneNumber.Normalize"/> gives it.</param>
/// <param name="TemplateName">The approved template's name.</param>
/// <param name="LanguageCode">The language the template was approved in.</param>
/// <param name="BodyParameters">The values for the body's <c>{{1}}</c>, <c>{{2}}</c>, ... in order.</param>
/// <param name="UrlButtonParameter">
/// The value for a dynamic-URL button, if the template has one. An Authentication template's "copy code" button is one:
/// it takes the code again.
/// </param>
public sealed record WhatsAppTemplateMessage(
    string To,
    string TemplateName,
    string LanguageCode,
    IReadOnlyList<string> BodyParameters,
    string? UrlButtonParameter = null);

/// <summary>What happened to a message handed to WhatsApp.</summary>
/// <param name="Sent">Whether WhatsApp accepted it. Acceptance is not delivery: the webhook reports what became of it.</param>
/// <param name="MessageId">WhatsApp's id for the message (<c>wamid...</c>), which the webhook's delivery reports carry; null when not accepted.</param>
public sealed record WhatsAppSendResult(bool Sent, string? MessageId = null);

/// <summary>Hands template messages to WhatsApp, the way <c>IMailSender</c> hands mail to a mail server.</summary>
public interface IWhatsAppSender
{
    /// <summary>
    /// Sends a template message. Never throws for a refusal, an unreachable service or missing settings: it logs why and
    /// reports <see cref="WhatsAppSendResult.Sent"/> false, so a caller that must answer every request the same way (sign-in)
    /// can.
    /// </summary>
    /// <param name="message">The template and its values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken);
}
