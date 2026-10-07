using ExamPlatform.Modules.Invite.Domain;

namespace ExamPlatform.Modules.Invite.Application.Dtos;

/// <summary>An invite as the inviting side sees it.</summary>
/// <remarks>
/// <c>EmailSent</c> says whether the invitation e-mail was handed to a mail server and is only set when an invite is
/// created. <c>WhatsAppSent</c> says whether the code was also handed to WhatsApp for the invited person's registered phone
/// number, and is set the same way. <c>InviteLink</c> is the link to pass on by hand and is only present when neither could
/// be sent, so the credential is not handed back to the inviter when it has already been delivered.
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
    bool WhatsAppSent = false
);

/// DTO for invite code.
public record InviteCodeDto(
    Guid Id,
    string Code,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    DateTime? RevokedAt
);
