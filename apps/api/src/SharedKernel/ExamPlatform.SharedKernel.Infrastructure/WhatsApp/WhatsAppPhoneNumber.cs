namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// Turns a phone number as a person typed it into the form the Cloud API takes: the country calling code followed by the
/// number, digits only (<c>919876543210</c>). The platform stores a number as it was entered, so this is applied when a
/// message is sent, never to what is stored or looked up.
/// </summary>
public static class WhatsAppPhoneNumber
{
    private const int MinDigits = 8;
    private const int MaxDigits = 15; // E.164's limit.
    private const int NationalNumberLength = 10; // India's, which DefaultCountryCode defaults to.
    private const int VisibleDigits = 2;

    /// <summary>
    /// Normalises a number. A leading <c>+</c> or <c>00</c> marks one that already has its country code. Otherwise it is a
    /// national number: a trunk <c>0</c> is dropped and <paramref name="defaultCountryCode"/> put in front, unless it is
    /// longer than a national number and already starts with that code (<c>919876543210</c>).
    /// </summary>
    /// <param name="raw">The number as entered; spaces, dashes, dots and brackets are allowed.</param>
    /// <param name="defaultCountryCode">The calling code to add to a national number, digits only; null or blank adds none.</param>
    /// <returns>The digits to send to, or null when this is not a phone number.</returns>
    public static string? Normalize(string? raw, string? defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        var international = text.StartsWith('+');
        var digits = new List<char>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsAsciiDigit(c))
            {
                digits.Add(c);
            }
            else if (!(c is ' ' or '-' or '.' or '(' or ')' || (c == '+' && i == 0)))
            {
                return null;
            }
        }

        var number = new string(digits.ToArray());
        if (!international && number.StartsWith("00", StringComparison.Ordinal))
        {
            number = number[2..];
            international = true;
        }

        var country = new string((defaultCountryCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (!international && country.Length > 0)
        {
            number = number.TrimStart('0');
            if (!(number.Length > NationalNumberLength && number.StartsWith(country, StringComparison.Ordinal)))
            {
                number = country + number;
            }
        }

        return number.Length is >= MinDigits and <= MaxDigits ? number : null;
    }

    /// <summary>A number for a log line: every digit but the last two hidden, so a log never holds a whole number.</summary>
    /// <param name="number">A number in any form; may be blank.</param>
    public static string Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return "***";
        }

        return number.Length <= VisibleDigits * 2
            ? new string('*', number.Length)
            : new string('*', number.Length - VisibleDigits) + number[^VisibleDigits..];
    }
}
