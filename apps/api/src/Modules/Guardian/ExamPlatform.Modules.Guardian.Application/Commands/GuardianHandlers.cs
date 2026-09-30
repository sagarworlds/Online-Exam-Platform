using System.ComponentModel.DataAnnotations;
using MediatR;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;
using ExamPlatform.Modules.Guardian.Application.Dtos;
using ExamPlatform.Modules.Guardian.Application.Ports;

namespace ExamPlatform.Modules.Guardian.Application.Commands;

/// Handler for CreateGuardianCommand.
public class CreateGuardianHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork) : IRequestHandler<CreateGuardianCommand, GuardianDto>
{
    public async Task<GuardianDto> Handle(CreateGuardianCommand command, CancellationToken cancellationToken)
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

/// Handler for LinkCandidateCommand.
public class LinkCandidateHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork) : IRequestHandler<LinkCandidateCommand, GuardianLinkDto>
{
    public async Task<GuardianLinkDto> Handle(LinkCandidateCommand command, CancellationToken cancellationToken)
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

/// Handler for VerifyGuardianLinkCommand.
public class VerifyGuardianLinkHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork) : IRequestHandler<VerifyGuardianLinkCommand>
{
    public async Task Handle(VerifyGuardianLinkCommand command, CancellationToken cancellationToken)
    {
        // Find all guardians and their links to match the token
        // In a real scenario, this would be optimized with a dedicated link repository
        // For now, we'll need to search through all guardians
        // This is a limitation of the current model - link repository would be better
        throw new NotImplementedException("Guardian link verification requires a dedicated link repository");
    }
}

/// Handler for RevokeGuardianLinkCommand.
public class RevokeGuardianLinkHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork) : IRequestHandler<RevokeGuardianLinkCommand>
{
    public async Task Handle(RevokeGuardianLinkCommand command, CancellationToken cancellationToken)
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

/// Handler for UnlinkCandidateCommand.
public class UnlinkCandidateHandler(IGuardianRepository repository, IGuardianUnitOfWork unitOfWork) : IRequestHandler<UnlinkCandidateCommand>
{
    public async Task Handle(UnlinkCandidateCommand command, CancellationToken cancellationToken)
    {
        var guardian = await repository.GetByIdOrThrowAsync(command.GuardianId, cancellationToken);
        guardian.UnlinkCandidate(command.CandidateId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
