using System.Net.Mail;

namespace ExamPlatform.Modules.Batch.Domain;

/// <summary>
/// Normalizes and checks the contact details of a roster member, so a batch and a CSV import agree on
/// what a valid e-mail address or phone number is (FR-50).
/// </summary>
public static class MemberContact
{
    /// <summary>The longest phone number stored (the column's width).</summary>
    public const int MaxPhoneLength = 20;

    /// <summary>The longest e-mail address stored (the column's width).</summary>
    public const int MaxEmailLength = 255;

    /// <summary>
    /// Trims and lower-cases an e-mail address. E-mail addresses are compared case-insensitively, so
    /// without this "A@x.com" and "a@x.com" would take two seats for one person.
    /// </summary>
    /// <param name="email">The address as entered.</param>
    /// <param name="normalized">The address in its stored form when this returns true.</param>
    /// <returns>True when the text is one plain address, with no display name or surrounding text.</returns>
    public static bool TryNormalizeEmail(string? email, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxEmailLength)
            return false;

        // TryCreate also accepts "Name <a@b.com>"; comparing the parsed address with the input rejects that.
        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed)
            return false;

        normalized = trimmed.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Strips the spaces and dashes people type inside a phone number and checks what is left: an optional
    /// <c>+</c> and 7 to 15 digits, not starting with zero.
    /// </summary>
    /// <param name="phone">The number as entered.</param>
    /// <param name="normalized">The number without separators when this returns true.</param>
    /// <returns>True when the number is plausible and fits the stored width.</returns>
    public static bool TryNormalizePhone(string? phone, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var stripped = phone.Replace(" ", "").Replace("-", "");
        var digits = stripped.StartsWith('+') ? stripped[1..] : stripped;
        if (stripped.Length > MaxPhoneLength
            || digits.Length is < 7 or > 15
            || digits[0] == '0'
            || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        normalized = stripped;
        return true;
    }
}
