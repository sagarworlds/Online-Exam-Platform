using System.ComponentModel.DataAnnotations;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Invite.Application.Dtos;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Application.Commands;

/// <summary>Handles <see cref="CreateInviteCommand"/>: invites an address to an exam and e-mails it the link (FR-14, FR-50a).</summary>
public sealed class CreateInviteHandler(
    IInviteRepository repository,
    IInviteUnitOfWork unitOfWork,
    IExamCatalog examCatalog,
    IInviteNotifier notifier,
    IInviteLinkBuilder linkBuilder,
    Clock clock,
    ILogger<CreateInviteHandler> logger)
{
    /// <summary>How long the first code of a new invite lives.</summary>
    public const int DefaultCodeExpiryHours = 72;

    /// <summary>Creates the invite with its first code, stores it, then tries to e-mail the link.</summary>
    /// <param name="command">The invite to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The invite; its link is included only when the e-mail could not be sent.</returns>
    /// <exception cref="InvalidInviteEmailError"><see cref="CreateInviteCommand.Email"/> is not a valid e-mail address.</exception>
    /// <exception cref="InviteExamNotFoundError">The exam does not exist.</exception>
    public async Task<InviteDto> HandleAsync(CreateInviteCommand command, CancellationToken cancellationToken)
    {
        if (!new EmailAddressAttribute().IsValid(command.Email))
            throw new InvalidInviteEmailError();

        var exam = await examCatalog.FindAsync(command.ExamId, cancellationToken)
            ?? throw new InviteExamNotFoundError(command.ExamId);

        var nowUtc = clock.UtcNow;
        var invite = new InviteAggregate(command.ExamId, command.BatchMemberId, command.Email.Trim(), command.CreatedByUserId, nowUtc);
        var code = invite.GenerateCode(DefaultCodeExpiryHours, nowUtc);

        repository.Add(invite);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the save, so a mail failure can never lose the invite; the inviter gets the link to pass on instead.
        var link = linkBuilder.Build(code.Code);
        var sent = await notifier.SendAsync(new InviteEmail(invite.Email, exam.Name, link, code.ExpiresAt), cancellationToken);
        if (!sent)
            logger.LogWarning("Invite {InviteId} was created but its e-mail was not sent; the inviter was given the link.", invite.Id);

        return invite.ToDto(exam.Name) with { EmailSent = sent, InviteLink = sent ? null : link };
    }
}

/// <summary>Handles <see cref="GenerateInviteCodeCommand"/>: adds a new code to an invite.</summary>
public sealed class GenerateInviteCodeHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Generates the code and stores it.</summary>
    /// <param name="command">The invite and the code lifetime.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InviteNotFoundError">No invite has that id.</exception>
    /// <exception cref="InvalidInviteExpiryError">The lifetime is outside the allowed range.</exception>
    public async Task<InviteCodeDto> HandleAsync(GenerateInviteCodeCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        var code = invite.GenerateCode(command.ExpiryHours, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new InviteCodeDto(code.Id, code.Code, code.ExpiresAt, code.UsedAt, code.RevokedAt);
    }
}

/// <summary>Handles <see cref="AcceptInviteCommand"/>: the signed-in candidate redeems an invite code (FR-14, FR-50a).</summary>
public sealed class AcceptInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Accepts the invite that owns the code, binding it to the caller.</summary>
    /// <param name="command">The code and who is accepting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accepted invite.</returns>
    /// <exception cref="InvalidInviteCodeError">No invite has the code, or it is used, revoked or expired.</exception>
    /// <exception cref="InviteEmailMismatchError">The caller's address is not the invited one.</exception>
    /// <exception cref="InviteStateError">The invite is no longer pending.</exception>
    public async Task<InviteDto> HandleAsync(AcceptInviteCommand command, CancellationToken cancellationToken)
    {
        var code = command.Code?.Trim().ToUpperInvariant();

        // An unknown code and a spent one answer the same way, so a caller cannot tell them apart.
        var invite = string.IsNullOrEmpty(code) ? null : await repository.GetByCodeAsync(code, cancellationToken);
        if (invite is null)
            throw new InvalidInviteCodeError("the code is not valid");

        invite.Accept(code, command.UserId, command.Email, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return invite.ToDto(examName: null);
    }
}

/// <summary>Handles <see cref="DeclineInviteCommand"/>: declines a pending invite.</summary>
public sealed class DeclineInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Declines the invite and stores the change.</summary>
    /// <param name="command">The invite to decline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InviteNotFoundError">No invite has that id.</exception>
    /// <exception cref="InviteStateError">The invite is no longer pending.</exception>
    public async Task HandleAsync(DeclineInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Decline(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="RevokeInviteCommand"/>: revokes an invite together with its codes.</summary>
public sealed class RevokeInviteHandler(IInviteRepository repository, IInviteUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Revokes the invite and stores the change.</summary>
    /// <param name="command">The invite to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InviteNotFoundError">No invite has that id.</exception>
    /// <exception cref="InviteStateError">The invite is already revoked or expired.</exception>
    public async Task HandleAsync(RevokeInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Revoke(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
