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

/// <summary>
/// A plain text message, written by a person. WhatsApp delivers one only to someone who has messaged the business number in the last 24
/// hours (the "customer service window"); outside it, only a template may be sent. The platform sends these only from the administrator's
/// WhatsApp test page.
/// </summary>
/// <param name="To">The recipient's number as <see cref="WhatsAppPhoneNumber.Normalize"/> gives it.</param>
/// <param name="Body">The text. WhatsApp allows at most 4096 characters.</param>
public sealed record WhatsAppTextMessage(string To, string Body);

/// <summary>What happened to a message handed to WhatsApp.</summary>
/// <param name="Sent">Whether WhatsApp accepted it. Acceptance is not delivery: the webhook reports what became of it.</param>
/// <param name="MessageId">WhatsApp's id for the message (<c>wamid...</c>), which the webhook's delivery reports carry; null when not accepted.</param>
/// <param name="Failure">Why it was not accepted, when it was not: what kind of problem and what to do about it. Null when it was accepted.</param>
public sealed record WhatsAppSendResult(bool Sent, string? MessageId = null, WhatsAppFailure? Failure = null);

/// <summary>Hands messages to WhatsApp, the way <c>IMailSender</c> hands mail to a mail server.</summary>
public interface IWhatsAppSender
{
    /// <summary>
    /// Sends a template message. Never throws for a refusal, an unreachable service or missing settings: it logs why and
    /// reports <see cref="WhatsAppSendResult.Sent"/> false (with the reason in <see cref="WhatsAppSendResult.Failure"/>), so a caller
    /// that must answer every request the same way (sign-in) can.
    /// </summary>
    /// <param name="message">The template and its values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a plain text message, which WhatsApp delivers only inside the 24-hour customer service window. Never throws, like
    /// <see cref="SendTemplateAsync"/>.
    /// </summary>
    /// <param name="message">The recipient and the text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<WhatsAppSendResult> SendTextAsync(WhatsAppTextMessage message, CancellationToken cancellationToken);
}
