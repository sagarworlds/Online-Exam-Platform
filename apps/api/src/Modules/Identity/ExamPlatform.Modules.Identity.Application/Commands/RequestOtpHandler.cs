using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="RequestOtpCommand"/>.</summary>
public sealed class RequestOtpHandler(
    IUserRepository userRepository,
    LoginEligibilityPolicy eligibilityPolicy,
    OtpChallengeIssuer otpChallengeIssuer,
    IIdentityUnitOfWork unitOfWork)
{
    /// <summary>
    /// Looks up the account by destination and issues the challenge its state allows: a
    /// Login code for an active account, or a Registration code for one still pending
    /// verification (verifying it proves control of the destination and activates the
    /// account, like the original registration code). An unknown destination, a suspended
    /// or deactivated account, and an account whose role requires password + 2FA all get a
    /// decoy challenge that is never sent and can never be verified.
    /// </summary>
    /// <param name="command">The channel and destination to send to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The id of the issued challenge, to be echoed back with the code on verify. A challenge
    /// id is returned in every case, so the response never reveals whether, or in what state,
    /// an account exists for the destination (FR-1, NFR-5).
    /// </returns>
    public async Task<Guid> HandleAsync(RequestOtpCommand command, CancellationToken cancellationToken)
    {
        var user = command.Channel == OtpChannel.Email
            ? await userRepository.GetByEmailAsync(command.Destination, cancellationToken)
            : await userRepository.GetByPhoneAsync(command.Destination, cancellationToken);

        var purpose = user?.Status == UserStatus.PendingVerification ? OtpPurpose.Registration : OtpPurpose.Login;

        var challengeId = user is not null && MayReceiveCode(user, purpose)
            ? await otpChallengeIssuer.IssueAsync(user.Id, command.Channel, command.Destination, purpose, cancellationToken)
            : await otpChallengeIssuer.IssueDecoyAsync(command.Channel, command.Destination, purpose, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return challengeId;
    }

    // The same policy that verify enforces decides who gets a real code, so a code is never
    // sent that verify would refuse to turn into a session.
    private bool MayReceiveCode(User user, OtpPurpose purpose) =>
        eligibilityPolicy.GetAccountViolation(user) is null
        && eligibilityPolicy.GetOtpPurposeViolation(user, purpose) is null;
}
