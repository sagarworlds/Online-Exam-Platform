using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// The default <see cref="IPasswordPolicy"/>. It favours length over composition rules
/// (no "one digit, one symbol" requirements), following NIST SP 800-63B: long passphrases
/// are both stronger and easier to remember than short complex passwords.
/// </summary>
public sealed class PasswordPolicy : IPasswordPolicy
{
    /// <summary>The fewest characters a password may have.</summary>
    public const int MinLength = 12;

    /// <summary>
    /// The most characters a password may have. Generous for any passphrase, while capping
    /// how much hashing work one request can make the server do.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// The shortest email local part that a password may not contain. Shorter ones (e.g.
    /// "a@example.com") would forbid too many ordinary passwords to be worth checking.
    /// </summary>
    public const int MinCheckedEmailLocalPartLength = 3;

    /// <inheritdoc />
    public void EnsureAcceptable(string? password, string? email)
    {
        if (password is null || password.Length < MinLength)
        {
            throw WeakPasswordError.TooShort(MinLength);
        }

        if (password.Length > MaxLength)
        {
            throw WeakPasswordError.TooLong(MaxLength);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw WeakPasswordError.Blank();
        }

        var localPart = EmailLocalPart(email);
        if (localPart.Length >= MinCheckedEmailLocalPartLength
            && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            throw WeakPasswordError.ContainsEmail();
        }
    }

    // The part before the last "@" (a quoted local part may itself contain one); the whole
    // value when there is no "@", and empty when the account has no email.
    private static string EmailLocalPart(string? email)
    {
        if (string.IsNullOrEmpty(email))
        {
            return string.Empty;
        }

        var at = email.LastIndexOf('@');
        return at < 0 ? email : email[..at];
    }
}
