using ExamPlatform.Modules.Guardian.Application.Dtos;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.Application.Commands;

/// <summary>Handles <see cref="CreateGuardianCommand"/>: registers a new guardian aggregate.</summary>
public sealed class CreateGuardianHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Creates the guardian and persists it.</summary>
    /// <param name="command">The guardian to register.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registered guardian.</returns>
    /// <exception cref="InvalidGuardianDetailsError">The e-mail address is invalid or the full name is blank.</exception>
    public async Task<GuardianDto> HandleAsync(CreateGuardianCommand command, CancellationToken cancellationToken)
    {
        var guardian = new GuardianAggregate(command.Email, command.FullName, command.Phone);
        repository.Add(guardian);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToDto(guardian);
    }

    private static GuardianDto MapToDto(GuardianAggregate guardian) =>
        new(guardian.Id, guardian.Email, guardian.Phone, guardian.FullName, guardian.CreatedAt, guardian.UpdatedAt);
}

/// <summary>Handles <see cref="LinkCandidateCommand"/>: links a guardian to a candidate.</summary>
public sealed class LinkCandidateHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Creates the pending link and persists it.</summary>
    /// <param name="command">The guardian and the candidate to link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pending link.</returns>
    /// <exception cref="GuardianNotFoundError">No guardian has that id.</exception>
    /// <exception cref="GuardianAlreadyLinkedError">The guardian already has a link to the candidate.</exception>
    public async Task<GuardianLinkDto> HandleAsync(LinkCandidateCommand command, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdAsync(command.GuardianId, cancellationToken)
            ?? throw new GuardianNotFoundError(command.GuardianId);
        var verificationToken = Guid.NewGuid().ToString("N");
        var link = guardian.LinkCandidate(command.CandidateId, command.CandidateEmail, verificationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new GuardianLinkDto(
            link.Id,
            link.GuardianId,
            link.CandidateId,
            link.CandidateEmail,
            link.Status,
            link.VerifiedAt,
            link.RevokedAt
        );
    }
}

/// <summary>Handles <see cref="RevokeGuardianLinkCommand"/>: revokes a guardian link to a candidate.</summary>
public sealed class RevokeGuardianLinkHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Revokes the link and persists the change.</summary>
    /// <param name="command">The guardian and the candidate whose link is revoked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GuardianNotFoundError">No guardian has that id.</exception>
    /// <exception cref="GuardianLinkNotFoundError">The guardian has no link to the candidate.</exception>
    /// <exception cref="GuardianLinkAlreadyRevokedError">The link was already revoked.</exception>
    public async Task HandleAsync(RevokeGuardianLinkCommand command, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdAsync(command.GuardianId, cancellationToken)
            ?? throw new GuardianNotFoundError(command.GuardianId);
        guardian.RevokeCandidateLink(command.CandidateId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="UnlinkCandidateCommand"/>: soft-deletes a guardian-candidate link.</summary>
public sealed class UnlinkCandidateHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Unlinks the candidate and persists the change.</summary>
    /// <param name="command">The guardian and the candidate to unlink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="GuardianNotFoundError">No guardian has that id.</exception>
    /// <exception cref="GuardianLinkNotFoundError">The guardian has no link to the candidate.</exception>
    public async Task HandleAsync(UnlinkCandidateCommand command, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdAsync(command.GuardianId, cancellationToken)
            ?? throw new GuardianNotFoundError(command.GuardianId);
        guardian.UnlinkCandidate(command.CandidateId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
