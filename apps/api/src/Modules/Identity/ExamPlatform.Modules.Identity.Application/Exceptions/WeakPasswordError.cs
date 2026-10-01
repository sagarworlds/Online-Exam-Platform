using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>
/// A new password does not meet the password policy (FR-3). Each factory names the rule
/// that failed, so the message tells the user what to change.
/// </summary>
public sealed class WeakPasswordError : DomainException
{
    private WeakPasswordError(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public override string ErrorCode => "weak_password";

    /// <summary>The password is missing or shorter than the minimum length.</summary>
    /// <param name="minLength">The minimum number of characters.</param>
    public static WeakPasswordError TooShort(int minLength) =>
        new($"The password must be at least {minLength} characters long.");

    /// <summary>The password is longer than the maximum length.</summary>
    /// <param name="maxLength">The maximum number of characters.</param>
    public static WeakPasswordError TooLong(int maxLength) =>
        new($"The password cannot be longer than {maxLength} characters.");

    /// <summary>The password consists only of whitespace.</summary>
    public static WeakPasswordError Blank() =>
        new("The password cannot consist only of spaces.");

    /// <summary>The password contains the part of the account's email address before the "@".</summary>
    public static WeakPasswordError ContainsEmail() =>
        new("The password cannot contain your email address.");
}
