using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// The single place that decides whether a user may be signed in, and by which kind of
/// one-time code, so the password and OTP flows can never disagree about locked accounts
/// or mandatory staff 2FA (FR-1, FR-3).
/// Each rule returns the violation instead of throwing it: a caller that has already
/// changed state (e.g. consumed an OTP challenge) saves that change first and only then
/// throws, so a refused code cannot simply be retried.
/// </summary>
public sealed class LoginEligibilityPolicy
{
    /// <summary>Checks that the account itself is allowed to sign in.</summary>
    /// <param name="user">The user trying to sign in.</param>
    /// <returns>
    /// An <see cref="AccountLockedError"/> when the account is suspended or deactivated;
    /// otherwise null.
    /// </returns>
    public DomainException? GetAccountViolation(User user) =>
        IsLocked(user.Status) ? new AccountLockedError() : null;

    /// <summary>
    /// Whether an account in this status is locked out of signing in. Also used to end
    /// already-issued sessions of such an account (FR-4), so signing in and staying signed
    /// in can never disagree about which statuses are locked.
    /// </summary>
    /// <param name="status">The account's current status.</param>
    public bool IsLocked(UserStatus status) => status is UserStatus.Suspended or UserStatus.Deactivated;

    /// <summary>Checks that a verified OTP of the given purpose may complete this user's sign-in.</summary>
    /// <param name="user">The user the challenge belongs to.</param>
    /// <param name="purpose">What the verified challenge was issued for.</param>
    /// <returns>
    /// An <see cref="OtpPurposeNotAllowedError"/> for a password-reset code; a
    /// <see cref="TwoFactorLoginRequiredError"/> for a login or registration code when one of
    /// the user's roles requires 2FA (only the second-factor step that follows a correct
    /// password may sign such a user in); otherwise null.
    /// </returns>
    public DomainException? GetOtpPurposeViolation(User user, OtpPurpose purpose) => purpose switch
    {
        OtpPurpose.PasswordReset => new OtpPurposeNotAllowedError(),
        OtpPurpose.Login or OtpPurpose.Registration when user.RequiresTwoFactor => new TwoFactorLoginRequiredError(),
        _ => null,
    };
}
