using MediatR;
using ExamPlatform.Modules.Guardian.Application.Dtos;

namespace ExamPlatform.Modules.Guardian.Application.Commands;

/// Command to create/register a new guardian.
public record CreateGuardianCommand(
    string Email,
    string FullName,
    string? Phone = null
) : IRequest<GuardianDto>;

/// Command to link a guardian to a candidate (generates verification token).
public record LinkCandidateCommand(
    Guid GuardianId,
    Guid CandidateId,
    string CandidateEmail
) : IRequest<GuardianLinkDto>;

/// Command to verify and confirm a guardian link.
public record VerifyGuardianLinkCommand(
    string VerificationToken
) : IRequest;

/// Command to revoke a guardian link to a candidate.
public record RevokeGuardianLinkCommand(
    Guid GuardianId,
    Guid CandidateId
) : IRequest;

/// Command to unlink (soft-delete) a guardian-candidate link.
public record UnlinkCandidateCommand(
    Guid GuardianId,
    Guid CandidateId
) : IRequest;
