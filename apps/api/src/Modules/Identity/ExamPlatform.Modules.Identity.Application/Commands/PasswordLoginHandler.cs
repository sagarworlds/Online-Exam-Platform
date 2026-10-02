using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="PasswordLoginCommand"/>.</summary>
public sealed class PasswordLoginHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    LoginEligibilityPolicy eligibilityPolicy,
    OtpChallengeIssuer otpChallengeIssuer,
    LoginSessionIssuer sessionIssuer,
    ISignInDiagnostics diagnostics,
    IIdentityUnitOfWork unitOfWork)
{
    /// <summary>
    /// Checks the password. If the account's role requires 2FA (FR-3), issues a
    /// second-factor OTP and returns a pending result instead of a token — the
    /// caller completes login via <see cref="VerifyOtpCommand"/>.
    /// </summary>
    /// <param name="command">The login credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidCredentialsError">No account matches, or the password is wrong.</exception>
    /// <exception cref="AccountLockedError">The account is suspended or deactivated.</exception>
    public async Task<AuthResult> HandleAsync(PasswordLoginCommand command, CancellationToken cancellationToken)
    {
        // All three refusals answer the same way, so the caller learns nothing about which it was; a developer's terminal does.
        var user = await userRepository.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null)
        {
            diagnostics.Explain(SignInHint.NoAccountForAddress, OtpChannel.Email, command.Email);
            throw new InvalidCredentialsError();
        }

        if (user.PasswordHash is null)
        {
            diagnostics.Explain(SignInHint.CandidateHasNoPassword, OtpChannel.Email, command.Email);
            throw new InvalidCredentialsError();
        }

        if (!passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            diagnostics.Explain(SignInHint.WrongPassword, OtpChannel.Email, command.Email);
            throw new InvalidCredentialsError();
        }

        if (eligibilityPolicy.GetAccountViolation(user) is { } accountViolation)
        {
            throw accountViolation;
        }

        if (user.RequiresTwoFactor)
        {
            var destination = user.Email ?? user.PhoneNumber!;
            var channel = user.Email is not null ? OtpChannel.Email : OtpChannel.Sms;
            var challengeId = await otpChallengeIssuer.IssueAsync(
                user.Id, channel, destination, OtpPurpose.TwoFactorStep, cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return AuthResult.TwoFactorRequired(challengeId);
        }

        var result = sessionIssuer.Issue(user, command.DeviceFingerprint, command.IpAddress);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
