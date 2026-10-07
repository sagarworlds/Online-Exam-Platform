using System.Globalization;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Invite.Infrastructure.WhatsApp;

/// <summary>
/// Sends an invitation's exam code to the invited person's phone as a WhatsApp template message. It is enabled only when WhatsApp is
/// switched on (<c>WhatsApp:Enabled</c>), a template is named and the platform has what it needs to send, and otherwise sends nothing. The code and link are credentials, so
/// they appear only in the message itself, and a number appears in a log only masked, like the e-mail and one-time-code senders.
/// </summary>
public sealed class WhatsAppInviteNotifier(
    IWhatsAppSender whatsApp,
    IOptions<InviteWhatsAppOptions> invite,
    IOptions<WhatsAppOptions> platform,
    ILogger<WhatsAppInviteNotifier> logger) : IInviteWhatsAppNotifier
{
    // WhatsApp refuses a template value of more than a few hundred characters; no exam name needs more.
    private const int MaxExamNameLength = 200;

    /// <inheritdoc />
    public bool IsEnabled => invite.Value.HasTemplate && platform.Value.IsEnabled && platform.Value.CanSend;

    /// <inheritdoc />
    public async Task<bool> SendAsync(InviteWhatsAppMessage message, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var to = WhatsAppPhoneNumber.Normalize(message.PhoneNumber, platform.Value.DefaultCountryCode);
        if (to is null)
        {
            logger.LogWarning(
                "An invitation was not sent on WhatsApp: {MaskedNumber} is not a phone number WhatsApp can reach.",
                WhatsAppPhoneNumber.Mask(message.PhoneNumber));
            return false;
        }

        var options = invite.Value;
        var result = await whatsApp.SendTemplateAsync(
            new WhatsAppTemplateMessage(
                to,
                options.TemplateName!,
                options.TemplateLanguage,
                [
                    Truncate(OneLine(message.ExamName), MaxExamNameLength),
                    message.Code,
                    message.Link,
                    message.ExpiresAtUtc.ToString("d MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture),
                ]),
            cancellationToken);

        if (result.Sent)
        {
            // The id is what the webhook's delivery report names, so a message that never arrived can be followed.
            logger.LogInformation(
                "An invitation was handed to WhatsApp for {MaskedNumber} as message {MessageId}.",
                WhatsAppPhoneNumber.Mask(to),
                result.MessageId);
        }
        else
        {
            // IWhatsAppSender has already logged why (not configured, refused, or unreachable).
            logger.LogWarning("An invitation could not be sent on WhatsApp to {MaskedNumber}.", WhatsAppPhoneNumber.Mask(to));
        }

        return result.Sent;
    }

    // A template value may not hold a line break, a tab or a run of spaces: WhatsApp refuses the whole message if it does. (Split on
    // whitespace rather than a generated regex: the generator would put a type of its own in this assembly, outside the module's namespace.)
    private static string OneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];
}
