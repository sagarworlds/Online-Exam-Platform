namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// Why a WhatsApp message was not sent or not delivered, grouped by what the operator should do about it. A name is stable and safe to
/// show; the text that goes with it is <see cref="WhatsAppFailure.Explanation"/>.
/// </summary>
public enum WhatsAppFailureKind
{
    /// <summary><c>WhatsApp:Enabled</c> is not <c>true</c>, so nothing is sent.</summary>
    SwitchedOff,

    /// <summary>A setting the sender needs is missing.</summary>
    NotConfigured,

    /// <summary>The recipient is not a phone number WhatsApp can reach.</summary>
    InvalidNumber,

    /// <summary>The access token is wrong, expired or revoked.</summary>
    InvalidToken,

    /// <summary>The access token is valid but may not send from this number.</summary>
    PermissionDenied,

    /// <summary>The phone number id is not one the token can see.</summary>
    WrongPhoneNumberId,

    /// <summary>The request URL is wrong: the API version is not a real one, or the base address has an extra part.</summary>
    WrongApiAddress,

    /// <summary>WhatsApp says part of the request is wrong, missing or too long; its details name which.</summary>
    InvalidRequest,

    /// <summary>WhatsApp rejected the recipient: a value it cannot use, or the sending number itself.</summary>
    InvalidRecipient,

    /// <summary>No template has that name in that language.</summary>
    TemplateNotFound,

    /// <summary>The template exists but Meta has paused or disabled it.</summary>
    TemplateUnavailable,

    /// <summary>The message does not match the template: the wrong number or shape of values.</summary>
    TemplateMismatch,

    /// <summary>The recipient is not on the test number's allowed list.</summary>
    RecipientNotAllowed,

    /// <summary>
    /// A free-text message was refused because the recipient has not messaged the business number in the last 24 hours; only a template
    /// may be sent first.
    /// </summary>
    ReEngagementRequired,

    /// <summary>WhatsApp accepted the message but could not deliver it: the number is not on WhatsApp, or has not accepted its terms.</summary>
    NotOnWhatsApp,

    /// <summary>The sending number is not registered or verified for the Cloud API.</summary>
    NumberNotRegistered,

    /// <summary>Meta could not charge for the message: the payment method needs attention.</summary>
    PaymentProblem,

    /// <summary>The business account or number is restricted or locked.</summary>
    AccountRestricted,

    /// <summary>WhatsApp is limiting how fast the number may send.</summary>
    RateLimited,

    /// <summary>A temporary problem on WhatsApp's side.</summary>
    ServiceUnavailable,

    /// <summary>The platform could not reach WhatsApp's servers at all.</summary>
    Unreachable,

    /// <summary>WhatsApp did not answer in time.</summary>
    TimedOut,

    /// <summary>WhatsApp refused the message for a reason not listed above; <see cref="WhatsAppFailure.MetaMessage"/> says what.</summary>
    Rejected,
}

/// <summary>
/// An accurate account of why a message did not go: what kind of failure, what to do about it in plain words, and what Meta itself said.
/// For the operator who is setting WhatsApp up, never for a candidate. It never names a recipient.
/// </summary>
/// <param name="Kind">The kind of failure.</param>
/// <param name="Explanation">What happened and what to do about it, written for the operator.</param>
/// <param name="MetaCode">Meta's error code, when the failure came from Meta.</param>
/// <param name="MetaMessage">Meta's own message, when the failure came from Meta.</param>
/// <param name="HttpStatus">The HTTP status Meta answered with, when it answered.</param>
public sealed record WhatsAppFailure(
    WhatsAppFailureKind Kind, string Explanation, int? MetaCode = null, string? MetaMessage = null, int? HttpStatus = null)
{
    /// <summary>WhatsApp is switched off.</summary>
    public static WhatsAppFailure SwitchedOff() =>
        new(WhatsAppFailureKind.SwitchedOff, "WhatsApp is switched off: WhatsApp__Enabled is not true, so nothing is sent.");

    /// <summary>Settings the sender needs are missing.</summary>
    /// <param name="missing">The names of the missing settings, as the properties of <see cref="WhatsAppOptions"/>.</param>
    public static WhatsAppFailure NotConfigured(IReadOnlyCollection<string> missing) =>
        new(
            WhatsAppFailureKind.NotConfigured,
            $"WhatsApp is not set up: {string.Join(", ", missing.Select(name => "WhatsApp__" + name))} {(missing.Count == 1 ? "is" : "are")} not set.");

    /// <summary>The recipient cannot be reached.</summary>
    public static WhatsAppFailure InvalidNumber() =>
        new(
            WhatsAppFailureKind.InvalidNumber,
            "That is not a phone number WhatsApp can reach. Write it with its country code (for example 919876543210), or without one if WhatsApp__DefaultCountryCode is the right one.");

    /// <summary>The configured base address is not a web address, so no request can even be made.</summary>
    /// <param name="baseUrl">What <c>WhatsApp:BaseUrl</c> holds.</param>
    public static WhatsAppFailure BadBaseUrl(string baseUrl) =>
        new(
            WhatsAppFailureKind.WrongApiAddress,
            $"WhatsApp__BaseUrl (\"{baseUrl}\") is not a web address. It must start with https://, or be left unset to use https://graph.facebook.com.");

    /// <summary>WhatsApp's servers could not be reached.</summary>
    /// <param name="host">The address that was tried.</param>
    /// <param name="detail">What the network said.</param>
    public static WhatsAppFailure Unreachable(string host, string detail) =>
        new(
            WhatsAppFailureKind.Unreachable,
            $"The API could not reach {host}: {detail}. Check the server's internet access and WhatsApp__BaseUrl (it is normally left unset).");

    /// <summary>WhatsApp did not answer in time.</summary>
    public static WhatsAppFailure TimedOut() =>
        new(WhatsAppFailureKind.TimedOut, "WhatsApp did not answer within 15 seconds. Try again; if it keeps happening, check the server's connection to graph.facebook.com.");
}
