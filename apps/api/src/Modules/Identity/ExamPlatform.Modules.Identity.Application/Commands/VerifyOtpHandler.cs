using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application;

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
    /// <exception cref="UserNotFoundError">The challenge's user no longer exists.</exception>
    public async Task<AuthResult> HandleAsync(VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        var challenge = await challengeRepository.GetByIdAsync(command.OtpChallengeId, cancellationToken)
            ?? throw new OtpChallengeNotFoundError();

        var suppliedCodeHash = codeGenerator.Hash(command.Code);
        challenge.Verify(suppliedCodeHash, clock.UtcNow);

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
