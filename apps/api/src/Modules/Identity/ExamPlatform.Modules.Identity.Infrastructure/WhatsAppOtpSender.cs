using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Delivers one-time codes to a phone number as a WhatsApp message, through an approved Authentication template whose body and
/// "copy code" button both carry the code. It handles the phone channel only: the number is put into the form the Cloud API
/// takes, and a number that is not a phone number is reported in the log and sent nowhere. Like <see cref="SmtpOtpSender"/> it
/// never throws for a delivery failure, since the caller answers every request the same way (FR-1), and it never logs the code.
/// </summary>
public sealed class WhatsAppOtpSender(IWhatsAppSender whatsApp, IOptions<WhatsAppOptions> options, ILogger<WhatsAppOtpSender> logger)
    : IOtpSender
{
    /// <inheritdoc />
    public async Task SendAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken)
    {
        var masked = ContactMasker.Mask(channel, destination);
        if (channel != OtpChannel.Sms)
        {
            logger.LogError("A {Channel} code for {MaskedDestination} was not sent: WhatsApp delivers to phone numbers only.", channel, masked);
            return;
        }

        var settings = options.Value;
        var to = WhatsAppPhoneNumber.Normalize(destination, settings.DefaultCountryCode);
        if (to is null)
        {
            logger.LogError("A code for {MaskedDestination} was not sent on WhatsApp: it is not a phone number WhatsApp can reach.", masked);
            return;
        }

        var result = await whatsApp.SendTemplateAsync(
            WhatsAppMessages.AuthenticationCode(to, settings.OtpTemplateName!, settings.OtpTemplateLanguage, code),
            cancellationToken);

        if (result.Sent)
        {
            // The id is what the webhook's delivery report names, so an operator can follow a code that never arrived.
            logger.LogInformation("A code for {MaskedDestination} was handed to WhatsApp as message {MessageId}.", masked, result.MessageId);
        }
        else
        {
            // IWhatsAppSender has already logged why (not configured, refused, or unreachable).
            logger.LogError("The WhatsApp code for {MaskedDestination} could not be delivered.", masked);
        }
    }
}
