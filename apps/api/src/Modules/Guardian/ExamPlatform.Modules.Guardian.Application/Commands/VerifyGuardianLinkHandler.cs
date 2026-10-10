using ExamPlatform.Modules.Guardian.Application.Dtos;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Domain;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Guardian.Application.Commands;

/// <summary>
/// Handles <see cref="VerifyGuardianLinkCommand"/>: a guardian confirms a candidate link with the code e-mailed to them (FR-39).
/// The caller is not signed in, because a guardian has no account yet, so the code is the only proof. A missing, unknown, reused or
/// expired code is refused, and nothing in the answer says which of these it was beyond the reason.
/// </summary>
/// <param name="repository">Finds the guardian who holds the link.</param>
/// <param name="unitOfWork">Saves the confirmation.</param>
/// <param name="clock">Gives the moment of confirmation, checked against the code's expiry.</param>
public sealed class VerifyGuardianLinkHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Confirms the link the code was issued for, and saves the change.</summary>
    /// <param name="command">The code the guardian presented.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The confirmed link.</returns>
    /// <exception cref="InvalidLinkTokenError">The code is missing, unknown, or has expired.</exception>
    /// <exception cref="GuardianLinkNotPendingError">The link was already confirmed or revoked.</exception>
    public async Task<GuardianLinkDto> HandleAsync(VerifyGuardianLinkCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            throw new InvalidLinkTokenError("it is missing");

        // Only the hash is stored, so the presented code is hashed before the lookup.
        var hash = GuardianLinkToken.Hash(command.Token);
        var guardian = await repository.GetByVerificationTokenHashAsync(hash, cancellationToken)
            ?? throw new InvalidLinkTokenError("it is not recognised");

        var link = guardian.VerifyCandidateLink(hash, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return GuardianLinkDto.From(link);
    }
}
