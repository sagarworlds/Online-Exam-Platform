using ExamPlatform.Modules.Invite.Domain;

namespace ExamPlatform.Modules.Invite.Application.Dtos;

/// DTO for invite details.
public record InviteDto(
    Guid Id,
    Guid ExamId,
    Guid BatchMemberId,
    string Email,
    InviteStatus Status,
    DateTime SentAt,
    DateTime? AcceptedAt,
    DateTime? DeclinedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// DTO for invite code.
public record InviteCodeDto(
    Guid Id,
    string Code,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    DateTime? RevokedAt
);
