using MediatR;
using ExamPlatform.Modules.Invite.Application.Dtos;

namespace ExamPlatform.Modules.Invite.Application.Commands;

/// Command to create a new invite.
public record CreateInviteCommand(
    Guid ExamId,
    Guid BatchMemberId,
    string Email,
    Guid CreatedByUserId
) : IRequest<InviteDto>;

/// Command to generate an invite code.
public record GenerateInviteCodeCommand(
    Guid InviteId,
    int ExpiryHours = 72
) : IRequest<InviteCodeDto>;

/// Command to accept an invite with a code.
public record AcceptInviteCommand(
    Guid InviteId,
    Guid InviteCodeId
) : IRequest;

/// Command to decline an invite.
public record DeclineInviteCommand(
    Guid InviteId
) : IRequest;

/// Command to revoke an invite and all its codes.
public record RevokeInviteCommand(
    Guid InviteId
) : IRequest;
