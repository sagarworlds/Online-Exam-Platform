using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="RegisterCandidateCommand"/>.</summary>
public sealed class RegisterCandidateHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    OtpChallengeIssuer otpChallengeIssuer,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Well-known name of the default role granted to every self-registered candidate.</summary>
    public const string CandidateRoleName = "Candidate";

    /// <summary>Creates the account, assigns the default Candidate role, and sends a confirmation OTP.</summary>
    /// <param name="command">The candidate's registration details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The id of the confirmation OTP challenge.</returns>
    /// <exception cref="ContactChannelMismatchError">The chosen channel's email or phone number was not given.</exception>
    /// <exception cref="DuplicateAccountError">An account already exists for the given email or phone.</exception>
    /// <exception cref="RoleNotFoundError">The default Candidate role has not been seeded.</exception>
    /// <exception cref="ContactRequiredError">Neither an email nor a phone number was given.</exception>
    /// <exception cref="InvalidContactError">The email or phone number is longer than the platform stores.</exception>
    public async Task<Guid> HandleAsync(RegisterCandidateCommand command, CancellationToken cancellationToken)
    {
        var destination = ResolveDestination(command);

        if (!string.IsNullOrWhiteSpace(command.Email) &&
            await userRepository.GetByEmailAsync(command.Email, cancellationToken) is not null)
        {
            throw new DuplicateAccountError();
        }

        if (!string.IsNullOrWhiteSpace(command.PhoneNumber) &&
            await userRepository.GetByPhoneAsync(command.PhoneNumber, cancellationToken) is not null)
        {
            throw new DuplicateAccountError();
        }

        var candidateRole = await roleRepository.GetByNameAsync(CandidateRoleName, cancellationToken)
            ?? throw new RoleNotFoundError();

        var user = User.Register(command.Email, command.PhoneNumber, command.DateOfBirth, command.DisplayName, clock.UtcNow);
        user.AssignRole(candidateRole);
        await userRepository.AddAsync(user, cancellationToken);

        var challengeId = await otpChallengeIssuer.IssueAsync(
            user.Id, command.OtpChannel, destination, OtpPurpose.Registration, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return challengeId;
    }

    // Checked before anything is looked up or added: a channel without its contact detail
    // used to reach the challenge insert with a null destination and fail there as a 500.
    private static string ResolveDestination(RegisterCandidateCommand command)
    {
        var destination = command.OtpChannel == OtpChannel.Email ? command.Email : command.PhoneNumber;
        return string.IsNullOrWhiteSpace(destination) ? throw new ContactChannelMismatchError(command.OtpChannel) : destination;
    }
}
