using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="VerifyOtpCommand"/>.</summary>
public sealed class VerifyOtpHandler(
    IOtpChallengeRepository challengeRepository,
    IUserRepository userRepository,
    IOtpCodeGenerator codeGenerator,
    LoginEligibilityPolicy eligibilityPolicy,
    LoginSessionIssuer sessionIssuer,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>
    /// Verifies the supplied code and, on success, starts a new session, provided the
    /// account may sign in and the challenge's purpose may complete its sign-in.
    /// </summary>
    /// <param name="command">The challenge id and supplied code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="OtpChallengeNotFoundError">No challenge matches the given id.</exception>
    /// <exception cref="OtpMismatchError">
    /// The code is wrong; the attempt has been recorded. Every code for a decoy challenge is wrong.
    /// </exception>
    /// <exception cref="OtpExpiredError">The challenge has expired.</exception>
    /// <exception cref="OtpAttemptsExceededError">The challenge has used up its attempts.</exception>
    /// <exception cref="OtpAlreadyUsedError">The challenge was already consumed (a replayed code).</exception>
    /// <exception cref="OtpSupersededError">A newer code was issued for the same destination and purpose.</exception>
    /// <exception cref="ConcurrencyConflictError">
    /// A parallel request changed the same challenge first (another verify, or a new code superseding it).
    /// </exception>
    /// <exception cref="UserNotFoundError">The challenge's user no longer exists.</exception>
    /// <exception cref="AccountLockedError">The account is suspended or deactivated.</exception>
    /// <exception cref="TwoFactorLoginRequiredError">
    /// The account's role requires 2FA, so only the second-factor code issued after its password may sign it in (FR-3).
    /// </exception>
    /// <exception cref="OtpPurposeNotAllowedError">The challenge was issued for a purpose that cannot sign anyone in.</exception>
    public async Task<AuthResult> HandleAsync(VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        var challenge = await challengeRepository.GetByIdAsync(command.OtpChallengeId, cancellationToken)
            ?? throw new OtpChallengeNotFoundError();

        var suppliedCodeHash = codeGenerator.Hash(command.Code);
        var outcome = challenge.Verify(suppliedCodeHash, clock.UtcNow);
        if (outcome != OtpVerificationOutcome.Verified)
        {
            // A wrong code has just counted against the challenge's attempt budget, and if
            // that count is not persisted every request starts from zero again and the
            // five-attempt lockout never engages (brute force, NFR-5).
            throw await SaveThenRejectAsync(outcome.ToError(), cancellationToken);
        }

        // From here on the challenge is consumed, so every refusal below is saved first:
        // a code refused here (e.g. a Login-purpose code for a staff account) must not be
        // retryable once the reason for the refusal goes away.
        if (challenge.UserId is not { } userId)
        {
            // Only a decoy has no user, and its code can never match, so this is unreachable;
            // answering as a wrong code keeps it indistinguishable from a real challenge anyway.
            throw await SaveThenRejectAsync(new OtpMismatchError(), cancellationToken);
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw await SaveThenRejectAsync(new UserNotFoundError(), cancellationToken);
        }

        var violation = eligibilityPolicy.GetAccountViolation(user)
            ?? eligibilityPolicy.GetOtpPurposeViolation(user, challenge.Purpose);
        if (violation is not null)
        {
            throw await SaveThenRejectAsync(violation, cancellationToken);
        }

        if (challenge.Purpose == OtpPurpose.Registration)
        {
            user.Activate();
        }

        var result = sessionIssuer.Issue(user, command.DeviceFingerprint, command.IpAddress);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    // Saves the challenge's changed state before the error is thrown, so the refusal
    // itself is durable; returns the error for the caller to throw.
    private async Task<DomainException> SaveThenRejectAsync(DomainException error, CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return error;
    }
}
