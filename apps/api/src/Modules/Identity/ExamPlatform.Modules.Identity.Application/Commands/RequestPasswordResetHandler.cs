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
    /// Issues and sends a reset token if the email matches an account.
    /// Intentionally does not reveal whether the account exists — the response
    /// is the same either way, so this endpoint cannot be used to enumerate
    /// registered email addresses.
    /// </summary>
    /// <param name="command">The account's email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null)
        {
            return;
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

        var resetToken = PasswordResetToken.Issue(user.Id, tokenHash, clock.UtcNow, Validity);
        await tokenRepository.AddAsync(resetToken, cancellationToken);

        // The reset link the user receives encodes both resetToken.Id and rawToken;
        // ResetPasswordCommand takes them back apart the same way OTP verify does.
        await sender.SendAsync(OtpChannel.Email, command.Email, $"{resetToken.Id}:{rawToken}", cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
