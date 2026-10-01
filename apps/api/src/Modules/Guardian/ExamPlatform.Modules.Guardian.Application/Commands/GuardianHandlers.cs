using System.ComponentModel.DataAnnotations;
using ExamPlatform.Modules.Guardian.Application.Dtos;
using ExamPlatform.Modules.Guardian.Application.Ports;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.Application.Commands;

/// <summary>Handles <see cref="CreateGuardianCommand"/>: registers a new guardian aggregate.</summary>
public sealed class CreateGuardianHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Creates the guardian and persists it.</summary>
    /// <param name="command">The guardian to register.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registered guardian.</returns>
    /// <exception cref="ArgumentException">The e-mail address is invalid or the full name is blank.</exception>
    public async Task<GuardianDto> HandleAsync(CreateGuardianCommand command, CancellationToken cancellationToken)
    {
        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(command.Email))
            throw new ArgumentException("Email must be a valid email address", nameof(command.Email));

        if (string.IsNullOrWhiteSpace(command.FullName))
            throw new ArgumentException("FullName cannot be empty or whitespace", nameof(command.FullName));

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
    public async Task<GuardianLinkDto> HandleAsync(LinkCandidateCommand command, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdOrThrowAsync(command.GuardianId, cancellationToken);
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

/// <summary>Handles <see cref="VerifyGuardianLinkCommand"/>: link verification is not implemented yet.</summary>
public sealed class VerifyGuardianLinkHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Verifies a guardian link by its token.</summary>
    /// <param name="command">The verification token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="NotImplementedException">Always; link verification needs a dedicated link repository.</exception>
    public Task HandleAsync(VerifyGuardianLinkCommand command, CancellationToken cancellationToken)
    {
        // Matching a token means searching the links of every guardian. That needs a dedicated
        // link repository, which the current model does not have.
        throw new NotImplementedException("Guardian link verification requires a dedicated link repository");
    }
}

/// <summary>Handles <see cref="RevokeGuardianLinkCommand"/>: revokes a guardian link to a candidate.</summary>
public sealed class RevokeGuardianLinkHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Revokes the link when it exists and persists the change.</summary>
    /// <param name="command">The guardian and the candidate whose link is revoked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(RevokeGuardianLinkCommand command, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdOrThrowAsync(command.GuardianId, cancellationToken);
        var link = guardian.GetCandidateLink(command.CandidateId);
        if (link != null)
        {
            link.Revoke();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>Handles <see cref="UnlinkCandidateCommand"/>: soft-deletes a guardian-candidate link.</summary>
public sealed class UnlinkCandidateHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork)
{
    /// <summary>Unlinks the candidate and persists the change.</summary>
    /// <param name="command">The guardian and the candidate to unlink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(UnlinkCandidateCommand command, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdOrThrowAsync(command.GuardianId, cancellationToken);
        guardian.UnlinkCandidate(command.CandidateId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
