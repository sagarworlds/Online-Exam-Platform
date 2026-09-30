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

/// Request to create an invite.
public record CreateInviteRequest(
    Guid ExamId,
    Guid BatchMemberId,
    string Email
);

/// Request to generate and retrieve a valid invite code.
public record GenerateInviteCodeRequest(
    int ExpiryHours = 72
);

/// Request to verify and accept an invite.
public record AcceptInviteRequest(
    Guid InviteCodeId
);
