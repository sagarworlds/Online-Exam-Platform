using ExamPlatform.Modules.Invite.Domain;

namespace ExamPlatform.Modules.Invite.Application.Dtos;

/// <summary>An invite as the inviting side sees it.</summary>
/// <remarks>
/// <c>EmailSent</c> says whether the invitation e-mail was handed to a mail server and is only set when an invite is
/// created. <c>WhatsAppSent</c> says whether the code was also handed to WhatsApp for the invited person's registered phone
/// number, and is set the same way. <c>InviteLink</c> and <c>InviteCode</c> (the code the link carries) are what to pass on by
/// hand and are only present when neither could be sent, so the credential is not handed back to the inviter when it has already
/// been delivered. An inviter who wants to hand a code over anyway asks for another one (<see cref="InviteCodeDto"/>).
/// </remarks>
public record InviteDto(
    Guid Id,
    Guid ExamId,
    string? ExamName,
    Guid? BatchMemberId,
    string Email,
    InviteStatus Status,
    DateTime SentAt,
    DateTime? AcceptedAt,
    DateTime? DeclinedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool EmailSent = false,
    string? InviteLink = null,
    bool WhatsAppSent = false,
    string? InviteCode = null
);

/// <summary>An invite code, as staff who ask for one to hand over see it.</summary>
/// <param name="Id">The code's id (the audit trail names it by this, never by the code).</param>
/// <param name="Code">The code the invited person enters on the invitation page.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="UsedAt">When it was redeemed, if it has been.</param>
/// <param name="RevokedAt">When it was revoked, if it has been.</param>
/// <param name="Link">The same code as the link the invitation e-mail carries; set when a code is generated.</param>
public record InviteCodeDto(
    Guid Id,
    string Code,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    DateTime? RevokedAt,
    string? Link = null
);
