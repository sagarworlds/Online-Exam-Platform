namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// How staff find someone by part of their phone number. The platform stores a number as it was entered (<c>+91 98765-43210</c>,
/// <c>098765 43210</c>, <c>9876543210</c>), so the stored text and the typed text are both reduced to the digits that name the person
/// before they are compared: spaces, dashes, dots and brackets, a trunk <c>0</c> and a country code in front do not stop a match.
/// </summary>
public static class PhoneSearch
{
    /// <summary>The fewest digits a search must hold. Fewer would match nearly everyone, and is the cheapest way to walk through numbers.</summary>
    public const int MinQueryDigits = 3;

    // A country calling code is one to three digits.
    private const int LongestCountryCode = 3;

    /// <summary>
    /// What a typed search stands for: its digits without a trunk <c>0</c>, and, when it was written with an international prefix
    /// (<c>+</c> or <c>00</c>), the same digits without the country code in front of them, since a number is often stored without one.
    /// </summary>
    /// <param name="text">What staff typed.</param>
    /// <returns>
    /// The forms of the search to look for; empty when the text is not a phone number (it holds letters or other characters) or
    /// holds fewer than <see cref="MinQueryDigits"/> digits.
    /// </returns>
    public static IReadOnlyList<string> Forms(string? text)
    {
        if (!TryDigits(text, out var digits, out var international) || digits.Length < MinQueryDigits)
        {
            return [];
        }

        var forms = new List<string> { digits };
        if (international)
        {
            // The country code's length is not known, and nor is whether the stored number has one, so each possible length is tried.
            for (var length = 1; length <= LongestCountryCode; length++)
            {
                if (digits.Length - length >= MinQueryDigits)
                {
                    forms.Add(digits[length..]);
                }
            }
        }

        return forms;
    }

    /// <summary>Whether a stored phone number holds one of the forms of a search.</summary>
    /// <param name="stored">The number as it was entered; null or blank never matches.</param>
    /// <param name="forms">What <see cref="Forms"/> gave for the search.</param>
    public static bool Matches(string? stored, IReadOnlyList<string> forms)
    {
        if (forms.Count == 0 || !TryDigits(stored, out var digits, out _))
        {
            return false;
        }

        foreach (var form in forms)
        {
            if (digits.Contains(form, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // The digits of a phone number written by a person, or false for text that is not one. A number may have spaces, dashes, dots and
    // brackets between its digits and a "+" at its start; anything else (a letter, an "@") means this is not a phone number, so an
    // e-mail address typed into the search is never read as one.
    private static bool TryDigits(string? text, out string digits, out bool international)
    {
        digits = string.Empty;
        international = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var builder = new System.Text.StringBuilder(trimmed.Length);
        var seenDigit = false;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (char.IsAsciiDigit(c))
            {
                builder.Append(c);
                seenDigit = true;
            }
            else if (c == '+' && !seenDigit)
            {
                // Only in front of the number; "(+91)" is fine, "98+76" is not.
                international = true;
            }
            else if (c is not (' ' or '-' or '.' or '(' or ')'))
            {
                return false;
            }
        }

        var number = builder.ToString();
        if (number.StartsWith("00", StringComparison.Ordinal))
        {
            number = number[2..];
            international = true;
        }

        // A trunk "0" ("098765 43210") is dialled inside a country and is no part of the number.
        digits = number.TrimStart('0');
        return digits.Length > 0;
    }
}
