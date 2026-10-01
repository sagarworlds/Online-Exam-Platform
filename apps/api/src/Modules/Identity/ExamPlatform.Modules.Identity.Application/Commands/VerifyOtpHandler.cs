using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="VerifyOtpCommand"/>.</summary>
public sealed class VerifyOtpHandler(
    IOtpChallengeRepository challengeRepository,
    IUserRepository userRepository,
    IOtpCodeGenerator codeGenerator,
    LoginSessionIssuer sessionIssuer,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Verifies the supplied code and, on success, starts a new session.</summary>
    /// <param name="command">The challenge id and supplied code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="OtpChallengeNotFoundError">No challenge matches the given id.</exception>
    /// <exception cref="OtpMismatchError">The code is wrong; the attempt has been recorded.</exception>
    /// <exception cref="OtpExpiredError">The challenge has expired.</exception>
    /// <exception cref="OtpAttemptsExceededError">The challenge has used up its attempts.</exception>
    /// <exception cref="OtpAlreadyUsedError">The challenge was already consumed (a replayed code).</exception>
    /// <exception cref="ConcurrencyConflictError">A parallel verify of the same challenge saved first.</exception>
    /// <exception cref="UserNotFoundError">The challenge's user no longer exists.</exception>
    public async Task<AuthResult> HandleAsync(VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        var challenge = await challengeRepository.GetByIdAsync(command.OtpChallengeId, cancellationToken)
            ?? throw new OtpChallengeNotFoundError();

        var suppliedCodeHash = codeGenerator.Hash(command.Code);
        var outcome = challenge.Verify(suppliedCodeHash, clock.UtcNow);
        if (outcome != OtpVerificationOutcome.Verified)
        {
            // Save before throwing: a wrong code has just counted against the challenge's
            // attempt budget, and if that count is not persisted every request starts from
            // zero again and the five-attempt lockout never engages (brute force, NFR-5).
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw outcome.ToError();
        }

        var userId = challenge.UserId ?? throw new UserNotFoundError();
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new UserNotFoundError();

        if (challenge.Purpose == OtpPurpose.Registration)
        {
            user.Activate();
        }

        var result = sessionIssuer.Issue(user, command.DeviceFingerprint, command.IpAddress);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
