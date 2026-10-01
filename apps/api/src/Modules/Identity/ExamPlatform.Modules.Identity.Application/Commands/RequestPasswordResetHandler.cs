using System.Security.Cryptography;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="RequestPasswordResetCommand"/>.</summary>
public sealed class RequestPasswordResetHandler(
    IUserRepository userRepository,
    IPasswordResetTokenRepository tokenRepository,
    IOtpSender sender,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    private static readonly TimeSpan Validity = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Issues and sends a reset token if the email matches an account that signs in with a
    /// password, and revokes that account's earlier unused reset links, so only the newest
    /// one works (FR-3).
    /// Intentionally does not reveal whether such an account exists — the response
    /// is the same either way, so this endpoint cannot be used to enumerate
    /// registered email addresses.
    /// </summary>
    /// <param name="command">The account's email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);

        // An account without a password (candidates sign in with a one-time code only, FR-1)
        // gets the same silent answer as an unknown email: a reset would give it a password
        // login it never had, and a different answer would tell a caller the account exists.
        if (user?.PasswordHash is null)
        {
            return;
        }

        // A set-based revoke, not tracked entities: parallel requests for one account all
        // revoke the same earlier links, and with tracked rows every loser of that race would
        // fail with a 409, which an unknown email never gets, so the difference would tell a
        // caller that the account exists (FR-1, NFR-5).
        var nowUtc = clock.UtcNow;
        await tokenRepository.RevokeOutstandingForUserAsync(user.Id, nowUtc, cancellationToken);

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

        var resetToken = PasswordResetToken.Issue(user.Id, tokenHash, nowUtc, Validity);
        await tokenRepository.AddAsync(resetToken, cancellationToken);

        // The reset link the user receives encodes both resetToken.Id and rawToken;
        // ResetPasswordCommand takes them back apart the same way OTP verify does.
        await sender.SendAsync(OtpChannel.Email, command.Email, $"{resetToken.Id}:{rawToken}", cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
