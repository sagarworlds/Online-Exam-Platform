using System.ComponentModel.DataAnnotations;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Application.Dtos;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Application.Commands;

/// <summary>
/// Handles <see cref="CreateInviteCommand"/>: invites an address to an exam and e-mails it the link, and, when the host sends
/// invitations on WhatsApp and the address belongs to an account with a phone number, sends the exam code there too (FR-14, FR-50a).
/// </summary>
public sealed class CreateInviteHandler(
    IInviteRepository repository,
    IInviteUnitOfWork unitOfWork,
    IExamCatalog examCatalog,
    IInviteNotifier notifier,
    IInviteWhatsAppNotifier whatsAppNotifier,
    IContactDirectory contacts,
    IInviteLinkBuilder linkBuilder,
    Clock clock,
    ILogger<CreateInviteHandler> logger)
{
    /// <summary>How long the first code of a new invite lives.</summary>
    public const int DefaultCodeExpiryHours = 72;

    /// <summary>Creates the invite with its first code, stores it, then tries to e-mail the link and to send the code on WhatsApp.</summary>
    /// <param name="command">The invite to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The invite; its link and code are included only when neither the e-mail nor the WhatsApp message could be sent.</returns>
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
        var whatsAppSent = await TrySendOnWhatsAppAsync(invite, exam.Name, code, link, cancellationToken);

        if (!sent && !whatsAppSent)
            logger.LogWarning("Invite {InviteId} was created but could not be sent; the inviter was given the link.", invite.Id);
        else if (!sent)
            logger.LogWarning("Invite {InviteId} was created and its e-mail was not sent; the code went to the invited person on WhatsApp.", invite.Id);

        var delivered = sent || whatsAppSent;
        return invite.ToDto(exam.Name) with
        {
            EmailSent = sent,
            WhatsAppSent = whatsAppSent,
            InviteLink = delivered ? null : link,
            InviteCode = delivered ? null : code.Code,
        };
    }

    // Sends the code to the phone registered on the invited address, when there is one and the host sends invitations on WhatsApp.
    // Best effort and after the save: the invite exists whatever happens here, so nothing in this may fail the request.
    private async Task<bool> TrySendOnWhatsAppAsync(
        InviteAggregate invite, string examName, InviteCode code, string link, CancellationToken cancellationToken)
    {
        // Asked first, so that nobody's number is read for a message that will not be sent.
        if (!whatsAppNotifier.IsEnabled)
            return false;

        try
        {
            var phoneNumber = await contacts.FindPhoneNumberByEmailAsync(invite.Email, cancellationToken);
            if (phoneNumber is null)
                return false;

            return await whatsAppNotifier.SendAsync(
                new InviteWhatsAppMessage(phoneNumber, examName, code.Code, link, code.ExpiresAt), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Invite {InviteId} was created but sending its code on WhatsApp failed.", invite.Id);
            return false;
        }
    }
}

/// <summary>
/// Handles <see cref="GenerateInviteCodeCommand"/>: adds a new code to a pending invite, for staff to hand over by hand. Each request
/// makes a new single-use code (the ones already sent are never read back), and the audit trail records who asked.
/// </summary>
public sealed class GenerateInviteCodeHandler(
    IInviteRepository repository, IInviteUnitOfWork unitOfWork, IInviteLinkBuilder linkBuilder, Clock clock)
{
    /// <summary>Generates the code and stores it.</summary>
    /// <param name="command">The invite and the code lifetime.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The code and the link that carries it.</returns>
    /// <exception cref="InviteNotFoundError">No invite has that id.</exception>
    /// <exception cref="InvalidInviteExpiryError">The lifetime is outside the allowed range.</exception>
    /// <exception cref="InviteStateError">The invite is no longer pending, so a new code could never be redeemed.</exception>
    public async Task<InviteCodeDto> HandleAsync(GenerateInviteCodeCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        var code = invite.AddCode(command.ExpiryHours, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new InviteCodeDto(code.Id, code.Code, code.ExpiresAt, code.UsedAt, code.RevokedAt, linkBuilder.Build(code.Code));
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
    /// <summary>Declines the invite on behalf of the invited address and stores the change.</summary>
    /// <param name="command">The invite to decline, and the declining account's e-mail address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InviteNotFoundError">No invite has that id.</exception>
    /// <exception cref="InviteEmailMismatchError">The declining account's address is not the invited one.</exception>
    /// <exception cref="InviteStateError">The invite is no longer pending.</exception>
    public async Task HandleAsync(DeclineInviteCommand command, CancellationToken cancellationToken)
    {
        var invite = await repository.GetByIdOrThrowAsync(command.InviteId, cancellationToken);
        invite.Decline(command.Email, clock.UtcNow);
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
