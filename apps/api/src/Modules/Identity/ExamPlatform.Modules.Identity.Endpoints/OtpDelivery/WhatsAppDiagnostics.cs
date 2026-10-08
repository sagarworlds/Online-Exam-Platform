using System.Security.Cryptography;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;

/// <summary>
/// The adapter behind the administrator's WhatsApp test: it reads the same settings, and sends through the same sender and the same
/// message shape, that sign-in uses, so a test that works means sign-in works. It lives with the delivery options it reads (the phone
/// provider) and, like them, belongs to the composition of this module rather than to a layer.
/// </summary>
public sealed class WhatsAppDiagnostics(
    IWhatsAppSender sender,
    IWhatsAppDeliveryTracker tracker,
    IOptions<WhatsAppOptions> whatsApp,
    IOptions<OtpDeliveryOptions> delivery,
    IConfiguration configuration) : IWhatsAppDiagnostics
{
    /// <inheritdoc />
    public WhatsAppStatusDto GetStatus()
    {
        var review = WhatsAppSetup.Review(whatsApp.Value, PhoneCodesUseWhatsApp(), configuration["Invite:WhatsApp:TemplateName"]);
        return new WhatsAppStatusDto(
            review.Enabled,
            review.CanSendMessages,
            review.CanSendTemplate,
            review.CanTrackDelivery,
            review.SignInCodesUseWhatsApp,
            review.InviteCodesUseWhatsApp,
            review.Settings.Select(s => new WhatsAppSettingDto(s.Setting, s.IsSet, s.Required, s.Purpose, s.Value)).ToList(),
            review.Problems,
            review.Notes);
    }

    /// <inheritdoc />
    public async Task<WhatsAppSendResultDto> SendAsync(
        WhatsAppTestMode mode, string phoneNumber, string? message, CancellationToken cancellationToken)
    {
        var settings = whatsApp.Value;
        var tracking = settings.CanReceiveWebhooks;

        var to = WhatsAppPhoneNumber.Normalize(phoneNumber, settings.DefaultCountryCode);
        if (to is null)
        {
            return Result(mode, null, tracking, WhatsAppFailure.InvalidNumber());
        }

        var masked = WhatsAppPhoneNumber.Mask(to);
        WhatsAppSendResult sent;
        string? note = null;
        if (mode == WhatsAppTestMode.SignInTemplate)
        {
            // The sender checks the switch and the credentials itself, but only this knows the template is needed, so say so before
            // calling out, in the order an operator fixes things: switch, credentials, template.
            if (!settings.IsEnabled)
            {
                return Result(mode, masked, tracking, WhatsAppFailure.SwitchedOff());
            }

            var missing = settings.MissingForOtp();
            if (missing.Count > 0)
            {
                return Result(mode, masked, tracking, WhatsAppFailure.NotConfigured(missing));
            }

            // A real-looking code so the message is the one a candidate would get; it is thrown away and opens nothing.
            var code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            sent = await sender.SendTemplateAsync(
                WhatsAppMessages.AuthenticationCode(to, settings.OtpTemplateName!, settings.OtpTemplateLanguage, code), cancellationToken);
            note = "A test code was sent. It cannot be used to sign in.";
        }
        else
        {
            sent = await sender.SendTextAsync(new WhatsAppTextMessage(to, message!), cancellationToken);
            note = tracking
                ? "WhatsApp accepted the message. That is not delivery: if this person has not messaged your WhatsApp number in the last 24 hours, WhatsApp reports it as failed a moment later and this page shows why."
                : "WhatsApp accepted the message. That is not delivery: if this person has not messaged your WhatsApp number in the last 24 hours, WhatsApp reports it as failed a moment later, but delivery reports are not set up, so this page cannot show it. Check the phone.";
        }

        return sent.Sent
            ? new WhatsAppSendResultDto(true, sent.MessageId, masked, mode.ToString(), tracking, note, null)
            : Result(mode, masked, tracking, sent.Failure ?? WhatsAppErrorGuide.Explain(null, null, null));
    }

    /// <inheritdoc />
    public WhatsAppDeliveryDto FindDelivery(string messageId)
    {
        var report = tracker.Find(messageId);
        return report is null
            ? new WhatsAppDeliveryDto(messageId, "NotReported", null, null, null)
            : new WhatsAppDeliveryDto(report.MessageId, report.Status, report.MaskedRecipient, report.UpdatedAtUtc, report.Failure is null ? null : Map(report.Failure));
    }

    private bool PhoneCodesUseWhatsApp() =>
        string.Equals(delivery.Value.PhoneProvider?.Trim(), OtpDeliveryOptions.WhatsApp, StringComparison.OrdinalIgnoreCase);

    private static WhatsAppSendResultDto Result(WhatsAppTestMode mode, string? masked, bool tracking, WhatsAppFailure failure) =>
        new(false, null, masked, mode.ToString(), tracking, null, Map(failure));

    private static WhatsAppFailureDto Map(WhatsAppFailure failure) =>
        new(failure.Kind.ToString(), failure.Explanation, failure.MetaCode, failure.MetaMessage, failure.HttpStatus);
}
