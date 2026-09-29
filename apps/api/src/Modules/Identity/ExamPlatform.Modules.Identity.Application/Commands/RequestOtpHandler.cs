using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="RequestOtpCommand"/>.</summary>
public sealed class RequestOtpHandler(
    IUserRepository userRepository,
    OtpChallengeIssuer otpChallengeIssuer,
    IIdentityUnitOfWork unitOfWork)
{
    /// <summary>Looks up the candidate by destination and sends them a login OTP.</summary>
    /// <param name="command">The channel and destination to send to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id of the issued challenge, to be echoed back with the code on verify.</returns>
    /// <exception cref="UserNotFoundError">No account exists for the given destination.</exception>
    public async Task<Guid> HandleAsync(RequestOtpCommand command, CancellationToken cancellationToken)
    {
        var user = command.Channel == OtpChannel.Email
            ? await userRepository.GetByEmailAsync(command.Destination, cancellationToken)
            : await userRepository.GetByPhoneAsync(command.Destination, cancellationToken);

        if (user is null)
        {
            throw new UserNotFoundError();
        }

        var challengeId = await otpChallengeIssuer.IssueAsync(
            user.Id, command.Channel, command.Destination, OtpPurpose.Login, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return challengeId;
    }
}
