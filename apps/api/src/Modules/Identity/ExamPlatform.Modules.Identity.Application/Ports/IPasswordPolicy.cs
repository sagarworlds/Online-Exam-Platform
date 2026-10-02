using ExamPlatform.Modules.Identity.Application.Exceptions;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>
/// Decides whether a new password may be set (FR-3). A port so the rules can be replaced,
/// e.g. by one that also checks a breached-password list, without touching the handlers
/// that set passwords.
/// </summary>
public interface IPasswordPolicy
{
    /// <summary>Checks a password that is about to be set on an account.</summary>
    /// <param name="password">The new plaintext password; null when the request carried none.</param>
    /// <param name="email">The account's email address, if it has one, so the password cannot simply repeat it.</param>
    /// <exception cref="WeakPasswordError">The password does not meet the policy.</exception>
    void EnsureAcceptable(string? password, string? email);
}
