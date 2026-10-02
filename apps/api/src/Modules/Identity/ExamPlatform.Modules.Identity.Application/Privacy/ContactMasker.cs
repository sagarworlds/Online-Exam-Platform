using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Privacy;

/// <summary>
/// Masks an email address or phone number so it can appear in logs without identifying the
/// person (NFR-6): enough is kept for a developer to tell destinations apart, never the
/// whole value.
/// </summary>
public static class ContactMasker
{
    /// <summary>What every hidden run of characters is replaced with in an email address.</summary>
    private const string Hidden = "***";

    /// <summary>How many trailing characters of a phone number are kept.</summary>
    private const int VisiblePhoneDigits = 2;

    /// <summary>
    /// Masks a destination for the given channel: an email keeps its first character and its
    /// domain (<c>j***@example.com</c>), a phone number keeps its last two characters
    /// (<c>********10</c>).
    /// </summary>
    /// <param name="channel">Which kind of destination <paramref name="destination"/> is.</param>
    /// <param name="destination">The email address or phone number; may be blank.</param>
    /// <returns>The masked value. It never equals a non-empty <paramref name="destination"/>.</returns>
    public static string Mask(OtpChannel channel, string? destination) => channel switch
    {
        OtpChannel.Email => MaskEmail(destination),
        OtpChannel.Sms => MaskPhone(destination),
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown OTP channel."),
    };

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return Hidden;
        }

        // Split at the last "@", as PasswordPolicy does, since a quoted local part may contain one.
        var at = email.LastIndexOf('@');
        if (at < 0)
        {
            return Hidden;
        }

        var localPart = email[..at];
        var domain = email[at..];

        // A one-character local part would otherwise be shown whole, as would the address.
        return localPart.Length > 1 ? localPart[0] + Hidden + domain : Hidden + domain;
    }

    private static string MaskPhone(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return Hidden;
        }

        // Too short to reveal anything and still hide most of it, so hide all of it.
        if (phoneNumber.Length <= VisiblePhoneDigits * 2)
        {
            return new string('*', phoneNumber.Length);
        }

        return new string('*', phoneNumber.Length - VisiblePhoneDigits) + phoneNumber[^VisiblePhoneDigits..];
    }
}
