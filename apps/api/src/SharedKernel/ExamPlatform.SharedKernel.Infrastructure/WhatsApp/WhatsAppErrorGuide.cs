namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// Turns what Meta reported (an HTTP status, an error code, a message) into a <see cref="WhatsAppFailure"/> that says in plain words what
/// is wrong and what to do about it. Meta reports a problem in one of two places, and they are classified by the same table: in the
/// answer to the send request, or later in a "failed" delivery report from the webhook (a number that is not on WhatsApp, and a
/// free-text message outside the 24-hour window, are only ever reported the second way).
/// </summary>
/// <remarks>
/// The table follows Meta's advice to classify on the error <em>code</em>: HTTP statuses, sub-codes and titles are not stable, so the
/// status is used only as a fallback and a sub-code only to word an expired token. The codes and their meanings were checked against
/// Meta's error-code reference; <c>131030</c>, <c>100</c> with sub-code 33 and <c>2500</c> are not on that page but were reproduced by
/// calling the API. A code not listed here is reported as <see cref="WhatsAppFailureKind.Rejected"/> with Meta's own words, never guessed at.
/// </remarks>
public static class WhatsAppErrorGuide
{
    /// <summary>Classifies a failure Meta reported.</summary>
    /// <param name="httpStatus">The HTTP status of the answer, or null for a failure reported by the webhook.</param>
    /// <param name="code">Meta's error code, if it gave one.</param>
    /// <param name="metaMessage">Meta's own message, if it gave one; shown alongside the explanation.</param>
    /// <param name="templateName">The template that was being sent, when one was, to name it in a template problem.</param>
    /// <param name="languageCode">The language that template was sent in.</param>
    /// <param name="subcode">Meta's error sub-code, if it gave one (deprecated by Meta; only used to word an expired token).</param>
    public static WhatsAppFailure Explain(
        int? httpStatus, int? code, string? metaMessage, string? templateName = null, string? languageCode = null, int? subcode = null)
    {
        var (kind, text) = Classify(httpStatus, code, metaMessage, templateName, languageCode, subcode);
        return new WhatsAppFailure(kind, text, code, metaMessage, httpStatus);
    }

    /// <summary>
    /// What Meta said, as one line for the operator to read and to quote to Meta support: its message, the details that usually name the
    /// field at fault (Meta's advice is to rely on these, not on titles), and its trace id.
    /// </summary>
    /// <param name="message">Meta's message.</param>
    /// <param name="details">Meta's <c>error_data.details</c>.</param>
    /// <param name="traceId">Meta's <c>fbtrace_id</c>.</param>
    /// <returns>The line, or null when Meta said nothing.</returns>
    public static string? Describe(string? message, string? details, string? traceId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message.Trim());
        }

        if (!string.IsNullOrWhiteSpace(details) && !string.Equals(details.Trim(), message?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(details.Trim());
        }

        if (!string.IsNullOrWhiteSpace(traceId))
        {
            parts.Add($"(trace id {traceId.Trim()})");
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    private static (WhatsAppFailureKind Kind, string Text) Classify(
        int? httpStatus, int? code, string? message, string? templateName, string? languageCode, int? subcode)
    {
        bool Mentions(string text) => message?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

        switch (code)
        {
            case 190:
                if (subcode == 463 || Mentions("Session has expired"))
                {
                    return (WhatsAppFailureKind.InvalidToken,
                        "The access token has expired. Create a new token for a system user (Business settings > System users), choose \"never expires\", and set it as WhatsApp__AccessToken. The one-day token on the API Setup page is the usual cause.");
                }

                if (Mentions("Cannot parse access token") || Mentions("Malformed access token"))
                {
                    return (WhatsAppFailureKind.InvalidToken,
                        "WhatsApp__AccessToken is not a token: it is truncated, has quotes or line breaks around it, or is a placeholder. Copy the whole token again, with no spaces, and set it.");
                }

                return (WhatsAppFailureKind.InvalidToken, InvalidTokenText);

            case 0 when Mentions("authenticate") || httpStatus == 401:
                return (WhatsAppFailureKind.InvalidToken, InvalidTokenText);

            case 10 or 3 or 131005 or (>= 200 and <= 299):
                return (WhatsAppFailureKind.PermissionDenied,
                    "The access token is valid but is not allowed to send from this WhatsApp account. In Business settings > System users, give the system user the WhatsApp account (full control) and generate the token with both the whatsapp_business_messaging and whatsapp_business_management permissions, then set it as WhatsApp__AccessToken.");

            case 100 when subcode == 33 || Mentions("Unsupported post request") || Mentions("does not exist"):
                return (WhatsAppFailureKind.WrongPhoneNumberId, WrongPhoneNumberIdText);

            case 33:
                return (WhatsAppFailureKind.WrongPhoneNumberId,
                    "WhatsApp says the sending phone number no longer exists, or the token may not send on behalf of it. Check WhatsApp__PhoneNumberId still exists in WhatsApp Manager and that the token belongs to a system user assigned to that WhatsApp account.");

            case 100 or 131008 or 135000:
                return (WhatsAppFailureKind.InvalidRequest,
                    "WhatsApp says part of the request is wrong, missing or too long, and names it in the details below. The same request will fail again until that is changed.");

            case 2500:
                return (WhatsAppFailureKind.WrongApiAddress, WrongApiAddressText);

            case 131009:
                return (WhatsAppFailureKind.InvalidRecipient,
                    "WhatsApp rejected a value in the request, most often the phone number. Enter it with its country code (for example 919876543210), or check WhatsApp__DefaultCountryCode if you leave the country code off.");

            case 131021:
                return (WhatsAppFailureKind.InvalidRecipient,
                    "The recipient is the WhatsApp number the platform sends from, and a number cannot message itself. Enter someone else's number.");

            case 131030:
                return (WhatsAppFailureKind.RecipientNotAllowed,
                    "The recipient is not on the allowed list. Meta's free test number can message only numbers you add under WhatsApp > API Setup > To > Manage phone number list, where each one confirms with a code; the list matches the digits exactly. A real, registered number has no such limit: put its Phone number ID in WhatsApp__PhoneNumberId.");

            case 131047:
                return (WhatsAppFailureKind.ReEngagementRequired,
                    "More than 24 hours have passed since this person last messaged your WhatsApp number, and WhatsApp only delivers free text inside that window. Ask them to send any message to your WhatsApp number first and try again, or use the sign-in code template, which works for anyone.");

            case 131026:
                return (WhatsAppFailureKind.NotOnWhatsApp,
                    "WhatsApp accepted the message but could not deliver it: the number is not on WhatsApp, or the person has not accepted WhatsApp's latest terms, or their app is very old. Check the number and its country code. Retrying will not help.");

            case 131037:
                return (WhatsAppFailureKind.NumberNotRegistered,
                    "The sending number has no approved display name yet. In WhatsApp Manager check the display name status of the number behind WhatsApp__PhoneNumberId and wait for approval.");

            case 131045 or 133010:
                return (WhatsAppFailureKind.NumberNotRegistered,
                    "The sending number is not registered for the WhatsApp Cloud API. Finish registering it in WhatsApp Manager (with its two-step verification PIN if one was set) and check that WhatsApp__PhoneNumberId is that number.");

            case 131042:
                return (WhatsAppFailureKind.PaymentProblem,
                    "WhatsApp could not charge for the message: there is a problem with the payment method on the WhatsApp Business account (none set, the credit line inactive, or the time zone or currency not set). Fix it in Business settings > WhatsApp Manager > Payment settings and send again.");

            case 131031 or 130497 or 368:
                return (WhatsAppFailureKind.AccountRestricted,
                    "The WhatsApp Business account is locked or restricted, or may not message people in the recipient's country. Open Meta Business Support Home or WhatsApp Manager to read the notice and appeal it; retrying will not help.");

            case 4 or 80007:
                return (WhatsAppFailureKind.RateLimited,
                    "The app or WhatsApp account has made too many API calls recently. Stop sending for a while and check the rate-limit panel in the Meta for Developers app dashboard.");

            case 130429:
                return (WhatsAppFailureKind.RateLimited,
                    "The sending number has reached its message throughput. Wait a moment and try again; for a single manual send this usually means the button was pressed repeatedly.");

            case 131056:
                return (WhatsAppFailureKind.RateLimited,
                    "Too many messages were sent to this same person in a short time (about one every six seconds). Wait a little before sending to that number again; other numbers are not affected.");

            case 131048:
                return (WhatsAppFailureKind.RateLimited,
                    "WhatsApp is limiting the number because many recent messages from it were reported as spam. Check the number's quality rating in WhatsApp Manager and stop any bulk or unwanted sends.");

            case 131049:
                return (WhatsAppFailureKind.Rejected,
                    "WhatsApp chose not to deliver this message to protect the person's experience. It is not a mistake in the request; do not retry straight away.");

            case 1 or 2 or 131016 or 133004 or 131000:
                return (WhatsAppFailureKind.ServiceUnavailable,
                    "WhatsApp reported a temporary problem on its side. Try again in a moment, and check Meta's WhatsApp Business Platform status page if it keeps happening.");

            case 131057:
                return (WhatsAppFailureKind.ServiceUnavailable,
                    "The WhatsApp Business account is in maintenance mode (for example while its throughput is being upgraded). Wait until it finishes and try again.");

            case 132000 or 132005 or 132012:
                return (WhatsAppFailureKind.TemplateMismatch,
                    $"The values sent do not fit {TemplateOrDefault(templateName)}: the wrong number of values, a value in the wrong format, or text that is too long once filled in. The sign-in template must be an Authentication template whose body and Copy code button each take the code; compare it with the template in WhatsApp Manager.");

            case 132001:
                return (WhatsAppFailureKind.TemplateNotFound,
                    $"No approved template named {Quoted(templateName)} exists in language {Quoted(languageCode)} on the WhatsApp account of the sending number. Copy the exact template name and language code from WhatsApp Manager into WhatsApp__OtpTemplateName and WhatsApp__OtpTemplateLanguage. \"en\" and \"en_US\" are different codes, and the template must be Approved.");

            case 132015 or 132016:
                return (WhatsAppFailureKind.TemplateUnavailable,
                    $"{CapitalisedTemplate(templateName)} has been paused or disabled by Meta, usually because of low quality ratings. Open it in WhatsApp Manager > Message templates to see why, and improve or replace it.");
        }

        if (httpStatus == 401)
        {
            return (WhatsAppFailureKind.InvalidToken, InvalidTokenText);
        }

        if (httpStatus >= 500)
        {
            return (WhatsAppFailureKind.ServiceUnavailable,
                "WhatsApp's servers answered with an error. Try again in a moment, and check Meta's WhatsApp Business Platform status page if it keeps happening.");
        }

        return (WhatsAppFailureKind.Rejected,
            code is { } known
                ? $"WhatsApp refused the message with error {known}, which this page has no advice for. What Meta said is below."
                : "WhatsApp refused the message and gave no error code. What Meta said, if anything, is below.");
    }

    private const string InvalidTokenText =
        "WhatsApp does not accept the access token: it is wrong, expired or has been revoked. Create a new token for a system user (Business settings > System users) and set it as WhatsApp__AccessToken. The one-day token on the API Setup page expires quickly and is the usual cause.";

    private const string WrongPhoneNumberIdText =
        "WhatsApp cannot find the sending number, or this token may not see it. WhatsApp__PhoneNumberId must be the Phone number ID shown under the number in WhatsApp > API Setup: not the phone number, the WhatsApp Business Account ID or the app ID. The token must belong to the same business.";

    private const string WrongApiAddressText =
        "WhatsApp could not route the request, so the address is wrong. WhatsApp__ApiVersion must be a real Graph version with a leading v (for example v23.0, and not a newer one than exists), and WhatsApp__BaseUrl should be just https://graph.facebook.com, which is what it is when left unset.";

    private static string Quoted(string? value) => string.IsNullOrWhiteSpace(value) ? "(none)" : $"\"{value}\"";

    private static string TemplateOrDefault(string? name) => string.IsNullOrWhiteSpace(name) ? "the template" : $"the template {Quoted(name)}";

    private static string CapitalisedTemplate(string? name) => string.IsNullOrWhiteSpace(name) ? "The template" : $"The template {Quoted(name)}";
}
