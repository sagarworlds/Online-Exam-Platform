using MediatR;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;
using ExamPlatform.Modules.Invite.Application.Dtos;
using ExamPlatform.Modules.Invite.Application.Ports;

namespace ExamPlatform.Modules.Invite.Application.Commands;

/// Handler for CreateInviteCommand.
public class CreateInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork) : IRequestHandler<CreateInviteCommand, InviteDto>
{
    public async Task<InviteDto> Handle(CreateInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = new InviteAggregate(
            command.ExamId,
            command.BatchMemberId,
            command.Email,
            command.CreatedByUserId
        );

        repository.Add(invite);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(invite);
    }

    private static InviteDto MapToDto(InviteAggregate invite) =>
        new(
            invite.Id,
            invite.ExamId,
            invite.BatchMemberId,
            invite.Email,
            invite.Status,
            invite.SentAt,
            invite.AcceptedAt,
            invite.DeclinedAt,
            invite.CreatedAt,
            invite.UpdatedAt
        );
}

/// Handler for GenerateInviteCodeCommand.
public class GenerateInviteCodeHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork) : IRequestHandler<GenerateInviteCodeCommand, InviteCodeDto>
{
    public async Task<InviteCodeDto> Handle(GenerateInviteCodeCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        var code = invite.GenerateCode(command.ExpiryHours);
        repository.Update(invite);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new InviteCodeDto(code.Id, code.Code, code.ExpiresAt, code.UsedAt, code.RevokedAt);
    }
}

/// Handler for AcceptInviteCommand.
public class AcceptInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork) : IRequestHandler<AcceptInviteCommand>
{
    public async Task Handle(AcceptInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Accept(command.InviteCodeId);
        repository.Update(invite);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// Handler for DeclineInviteCommand.
public class DeclineInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork) : IRequestHandler<DeclineInviteCommand>
{
    public async Task Handle(DeclineInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Decline();
        repository.Update(invite);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// Handler for RevokeInviteCommand.
public class RevokeInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork) : IRequestHandler<RevokeInviteCommand>
{
    public async Task Handle(RevokeInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Revoke();
        repository.Update(invite);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
