using System.Security.Cryptography;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="ResetPasswordCommand"/>.</summary>
public sealed class ResetPasswordHandler(
    IPasswordResetTokenRepository tokenRepository,
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Validates the token and sets the account's new password.</summary>
    /// <param name="command">The token and new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="PasswordResetTokenInvalidError">The token is unknown, expired, already used, or does not match.</exception>
    /// <exception cref="UserNotFoundError">The token's user no longer exists.</exception>
    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var token = await tokenRepository.GetByIdAsync(command.PasswordResetTokenId, cancellationToken)
            ?? throw new PasswordResetTokenInvalidError();

        var suppliedHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(command.Token)));
        if (!token.IsUsable(clock.UtcNow) || !string.Equals(suppliedHash, token.TokenHash, StringComparison.Ordinal))
        {
            throw new PasswordResetTokenInvalidError();
        }

        var user = await userRepository.GetByIdAsync(token.UserId, cancellationToken)
            ?? throw new UserNotFoundError();

        user.SetPasswordHash(passwordHasher.Hash(command.NewPassword));
        token.Consume(clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
