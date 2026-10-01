using System.ComponentModel.DataAnnotations;
using ExamPlatform.Modules.Invite.Application.Dtos;
using ExamPlatform.Modules.Invite.Application.Ports;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Application.Commands;

/// <summary>Handles <see cref="CreateInviteCommand"/>: creates a new invite aggregate.</summary>
public sealed class CreateInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork)
{
    /// <summary>Creates the invite and persists it.</summary>
    /// <param name="command">The invite to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created invite.</returns>
    /// <exception cref="ArgumentException"><see cref="CreateInviteCommand.Email"/> is not a valid e-mail address.</exception>
    public async Task<InviteDto> HandleAsync(CreateInviteCommand command, CancellationToken cancellationToken)
    {
        var emailValidator = new EmailAddressAttribute();
        if (!emailValidator.IsValid(command.Email))
            throw new ArgumentException("Email must be a valid email address", nameof(command.Email));

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

/// <summary>Handles <see cref="GenerateInviteCodeCommand"/>: adds a new code to an invite.</summary>
public sealed class GenerateInviteCodeHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork)
{
    /// <summary>Generates the code and persists it.</summary>
    /// <param name="command">The invite and the code lifetime.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated code.</returns>
    public async Task<InviteCodeDto> HandleAsync(GenerateInviteCodeCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        var code = invite.GenerateCode(command.ExpiryHours);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new InviteCodeDto(code.Id, code.Code, code.ExpiresAt, code.UsedAt, code.RevokedAt);
    }
}

/// <summary>Handles <see cref="AcceptInviteCommand"/>: redeems an invite code.</summary>
public sealed class AcceptInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork)
{
    /// <summary>Accepts the invite with the given code and persists the change.</summary>
    /// <param name="command">The invite and the code that is redeemed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(AcceptInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Accept(command.InviteCodeId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="DeclineInviteCommand"/>: declines a pending invite.</summary>
public sealed class DeclineInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork)
{
    /// <summary>Declines the invite and persists the change.</summary>
    /// <param name="command">The invite to decline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(DeclineInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Decline();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="RevokeInviteCommand"/>: revokes an invite together with its codes.</summary>
public sealed class RevokeInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork)
{
    /// <summary>Revokes the invite and persists the change.</summary>
    /// <param name="command">The invite to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(RevokeInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Revoke();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
