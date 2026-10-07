namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// Settings for the WhatsApp Business Platform (Meta's Cloud API), bound from the <c>WhatsApp</c> configuration section.
/// Every value comes from the Meta app and its WhatsApp Business Account; none has a usable default except the API
/// version, the country code and the template language, which are conveniences.
/// </summary>
public sealed class WhatsAppOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// A permanent access token for the Cloud API, from a system user of the business that owns the WhatsApp Business Account
    /// (a temporary token from the app dashboard expires within a day). A secret: never log it.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>The id of the sending phone number (not the number itself), shown under the number in the app dashboard.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>
    /// The Meta app's secret, which signs every webhook call (<c>X-Hub-Signature-256</c>). Without it the webhook refuses to
    /// accept anything: an unsigned call could be anyone's. A secret.
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>
    /// A string chosen here and typed into the app dashboard when the webhook is set up; Meta sends it back to prove the
    /// callback URL is the one the operator meant. A secret.
    /// </summary>
    public string? WebhookVerifyToken { get; set; }

    /// <summary>The name of the approved <c>Authentication</c> template that carries a one-time code. Blank turns code delivery off.</summary>
    public string? OtpTemplateName { get; set; }

    /// <summary>The language the one-time-code template was approved in (for example <c>en</c>); it must match that template.</summary>
    public string OtpTemplateLanguage { get; set; } = "en";

    /// <summary>
    /// The country calling code added to a number written without one (for example <c>9876543210</c>), digits only. Leave blank
    /// to accept only numbers that already carry theirs. The default is India's, where the platform's candidates are.
    /// </summary>
    public string? DefaultCountryCode { get; set; } = "91";

    /// <summary>The Graph API version the calls are made against; Meta supports each version for about two years.</summary>
    public string ApiVersion { get; set; } = "v23.0";

    /// <summary>The Graph API's address; changed only to point a test at a stand-in.</summary>
    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    /// <summary>Whether enough is set to call the Cloud API at all.</summary>
    public bool CanSend => Has(AccessToken) && Has(PhoneNumberId);

    /// <summary>Whether enough is set to deliver one-time codes.</summary>
    public bool CanSendOtp => CanSend && Has(OtpTemplateName);

    /// <summary>Whether enough is set to answer Meta's webhook verification and to check the signature of what it sends.</summary>
    public bool CanReceiveWebhooks => Has(AppSecret) && Has(WebhookVerifyToken);

    /// <summary>The settings still needed before one-time codes can be delivered, by name (empty when none is).</summary>
    public IReadOnlyList<string> MissingForOtp()
    {
        var missing = new List<string>();
        if (!Has(AccessToken))
        {
            missing.Add(nameof(AccessToken));
        }

        if (!Has(PhoneNumberId))
        {
            missing.Add(nameof(PhoneNumberId));
        }

        if (!Has(OtpTemplateName))
        {
            missing.Add(nameof(OtpTemplateName));
        }

        return missing;
    }

    private static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);
}
