using System.Security.Cryptography;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="ResetPasswordCommand"/>.</summary>
public sealed class ResetPasswordHandler(
    IPasswordResetTokenRepository tokenRepository,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IPasswordPolicy passwordPolicy,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>
    /// Validates the token and sets the account's new password. A reset is how an account
    /// is recovered from someone who knows the old password, so it also ends every session
    /// the account has and withdraws its other unused reset links (FR-3, FR-4).
    /// </summary>
    /// <param name="command">The token and new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PasswordResetTokenInvalidError">
    /// The token is unknown, expired, already used, revoked, or does not match, or its account
    /// does not sign in with a password.
    /// </exception>
    /// <exception cref="UserNotFoundError">The token's user no longer exists.</exception>
    /// <exception cref="WeakPasswordError">The new password does not meet the password policy; the link stays usable.</exception>
    /// <exception cref="ConcurrencyConflictError">Another request used or revoked the token at the same time.</exception>
    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var token = await tokenRepository.GetByIdAsync(command.PasswordResetTokenId, cancellationToken)
            ?? throw new PasswordResetTokenInvalidError();

        // JSON binding does not enforce the non-nullable annotation, so a request without a
        // token arrives as null; it is treated as a token that does not match, not as a 500.
        var suppliedHash = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(command.Token ?? string.Empty)));
        if (!token.IsUsable(nowUtc) || !string.Equals(suppliedHash, token.TokenHash, StringComparison.Ordinal))
        {
            throw new PasswordResetTokenInvalidError();
        }

        var user = await userRepository.GetByIdAsync(token.UserId, cancellationToken)
            ?? throw new UserNotFoundError();

        // Links issued before password-less accounts stopped getting them must not give such
        // an account (an OTP-only candidate, FR-1) a password login either.
        if (user.PasswordHash is null)
        {
            throw new PasswordResetTokenInvalidError();
        }

        // Checked before anything changes, so a refused password leaves the link usable and
        // the user can simply try a stronger one.
        passwordPolicy.EnsureAcceptable(command.NewPassword, user.Email);

        user.SetPasswordHash(passwordHasher.Hash(command.NewPassword));
        token.Consume(nowUtc);

        foreach (var other in await tokenRepository.GetOutstandingForUserAsync(user.Id, nowUtc, cancellationToken))
        {
            if (other.Id != token.Id)
            {
                other.Revoke(nowUtc);
            }
        }

        user.RevokeAllSessions(nowUtc, SessionRevocationReason.PasswordReset);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
