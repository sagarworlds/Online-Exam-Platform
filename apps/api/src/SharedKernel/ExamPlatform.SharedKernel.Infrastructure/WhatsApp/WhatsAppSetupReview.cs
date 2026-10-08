namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>One setting WhatsApp depends on, and whether it is in place.</summary>
/// <param name="Setting">The environment variable that sets it, for example <c>WhatsApp__AccessToken</c>.</param>
/// <param name="IsSet">Whether it has a value (for the master switch, whether it is on).</param>
/// <param name="Required">Whether nothing can be sent without it.</param>
/// <param name="Purpose">What it is for, in plain words.</param>
/// <param name="Value">The value, for settings that are not secret (a template name, a language, a version); null for a secret or one that is not set.</param>
public sealed record WhatsAppSettingStatus(string Setting, bool IsSet, bool Required, string Purpose, string? Value);

/// <summary>
/// Where the WhatsApp configuration stands, worked out from the settings alone, with no call to Meta: what is missing, and what is
/// possible. It never contains a secret.
/// </summary>
/// <param name="Enabled">Whether the master switch is on.</param>
/// <param name="CanSendMessages">Whether the switch is on and the access token and phone number id are set, so a message can be attempted.</param>
/// <param name="CanSendTemplate">Whether <paramref name="CanSendMessages"/> and the sign-in template is named too.</param>
/// <param name="CanTrackDelivery">Whether the webhook is configured, so Meta's delivery reports are accepted.</param>
/// <param name="SignInCodesUseWhatsApp">Whether phone sign-in codes are routed to WhatsApp.</param>
/// <param name="InviteCodesUseWhatsApp">Whether invitations also send the exam code on WhatsApp.</param>
/// <param name="Settings">Every setting and its state.</param>
/// <param name="Problems">What stops WhatsApp working, as sentences.</param>
/// <param name="Notes">Optional things worth knowing, as sentences.</param>
public sealed record WhatsAppSetupReview(
    bool Enabled,
    bool CanSendMessages,
    bool CanSendTemplate,
    bool CanTrackDelivery,
    bool SignInCodesUseWhatsApp,
    bool InviteCodesUseWhatsApp,
    IReadOnlyList<WhatsAppSettingStatus> Settings,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notes);

/// <summary>Reviews the WhatsApp configuration for the administrator's test page.</summary>
public static class WhatsAppSetup
{
    /// <summary>Reviews the settings.</summary>
    /// <param name="options">The <c>WhatsApp</c> settings.</param>
    /// <param name="phoneCodesUseWhatsApp">Whether <c>Identity:OtpDelivery:PhoneProvider</c> routes phone codes to WhatsApp.</param>
    /// <param name="inviteTemplateName">The invitation template, <c>Invite:WhatsApp:TemplateName</c>, or null or blank when unset.</param>
    public static WhatsAppSetupReview Review(WhatsAppOptions options, bool phoneCodesUseWhatsApp, string? inviteTemplateName)
    {
        var hasInviteTemplate = !string.IsNullOrWhiteSpace(inviteTemplateName);
        var hasSecret = !string.IsNullOrWhiteSpace(options.AppSecret);
        var hasVerifyToken = !string.IsNullOrWhiteSpace(options.WebhookVerifyToken);
        var hasToken = !string.IsNullOrWhiteSpace(options.AccessToken);
        var hasPhoneNumberId = !string.IsNullOrWhiteSpace(options.PhoneNumberId);
        var hasTemplate = !string.IsNullOrWhiteSpace(options.OtpTemplateName);

        var settings = new List<WhatsAppSettingStatus>
        {
            new("WhatsApp__Enabled", options.IsEnabled, true, "The master switch. Nothing is sent unless it is true.",
                options.IsEnabled ? "true" : options.Enabled is false ? "false" : null),
            new("WhatsApp__AccessToken", hasToken, true,
                "Lets the platform send as your business. It must be a permanent token from a system user, not the dashboard's one-day token.", null),
            new("WhatsApp__PhoneNumberId", hasPhoneNumberId, true,
                "The id of the sending number (not the number itself), shown under it in WhatsApp > API Setup.",
                hasPhoneNumberId ? options.PhoneNumberId!.Trim() : null),
            new("WhatsApp__OtpTemplateName", hasTemplate, false,
                "The approved Authentication template that carries a sign-in code. Needed for phone sign-in codes and for the template test.",
                hasTemplate ? options.OtpTemplateName!.Trim() : null),
            new("WhatsApp__OtpTemplateLanguage", !string.IsNullOrWhiteSpace(options.OtpTemplateLanguage), false,
                "The language code that template was approved in. It must match exactly (en is not en_US).", options.OtpTemplateLanguage),
            new("Identity__OtpDelivery__PhoneProvider", phoneCodesUseWhatsApp, false,
                "Set to WhatsApp so that sign-in codes for phone numbers go on WhatsApp.", phoneCodesUseWhatsApp ? "WhatsApp" : null),
            new("Invite__WhatsApp__TemplateName", hasInviteTemplate, false,
                "The approved template that sends an invitation's exam code on WhatsApp.",
                hasInviteTemplate ? inviteTemplateName!.Trim() : null),
            new("WhatsApp__AppSecret", hasSecret, false, "Signs Meta's delivery reports so they can be trusted. Needed for the webhook.", null),
            new("WhatsApp__WebhookVerifyToken", hasVerifyToken, false,
                "A string you choose and also enter in the Meta dashboard when you register the webhook.", null),
            new("WhatsApp__DefaultCountryCode", !string.IsNullOrWhiteSpace(options.DefaultCountryCode), false,
                "Added to a number written without a country code.", options.DefaultCountryCode),
            new("WhatsApp__ApiVersion", !string.IsNullOrWhiteSpace(options.ApiVersion), false,
                "The Graph API version. Meta retires each version after about two years.", options.ApiVersion),
            new("WhatsApp__BaseUrl", !string.IsNullOrWhiteSpace(options.BaseUrl), false,
                "Meta's API address. Normally left as it is.", options.BaseUrl),
        };

        var problems = new List<string>();
        if (!options.IsEnabled)
        {
            problems.Add("WhatsApp is switched off: WhatsApp__Enabled is not true, so nothing is sent.");
        }

        if (!hasToken)
        {
            problems.Add("WhatsApp__AccessToken is not set.");
        }

        if (!hasPhoneNumberId)
        {
            problems.Add("WhatsApp__PhoneNumberId is not set.");
        }

        if (hasSecret != hasVerifyToken)
        {
            problems.Add("Only one of WhatsApp__AppSecret and WhatsApp__WebhookVerifyToken is set; the webhook needs both.");
        }

        var notes = new List<string>();
        if (!hasTemplate)
        {
            notes.Add("WhatsApp__OtpTemplateName is not set, so the sign-in code template cannot be used and phone sign-in codes cannot be sent.");
        }

        if (!phoneCodesUseWhatsApp)
        {
            notes.Add("Sign-in codes for phone numbers are not sent on WhatsApp: Identity__OtpDelivery__PhoneProvider is not WhatsApp.");
        }

        if (!hasInviteTemplate)
        {
            notes.Add("Invitations do not send their exam code on WhatsApp: Invite__WhatsApp__TemplateName is not set. That is optional.");
        }

        if (!hasSecret && !hasVerifyToken)
        {
            notes.Add(
                "Delivery reports are off: without WhatsApp__AppSecret and WhatsApp__WebhookVerifyToken and a webhook registered in the Meta dashboard, "
                + "a message WhatsApp accepted cannot be confirmed as delivered, and a number that is not on WhatsApp is never reported.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultCountryCode))
        {
            notes.Add("WhatsApp__DefaultCountryCode is blank, so every number must be written with its country code.");
        }

        var canSend = options.IsEnabled && hasToken && hasPhoneNumberId;
        return new WhatsAppSetupReview(
            options.IsEnabled,
            canSend,
            canSend && hasTemplate,
            options.CanReceiveWebhooks,
            phoneCodesUseWhatsApp,
            hasInviteTemplate,
            settings,
            problems,
            notes);
    }
}
