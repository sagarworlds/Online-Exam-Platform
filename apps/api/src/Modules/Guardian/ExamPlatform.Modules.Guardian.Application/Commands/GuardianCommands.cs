namespace ExamPlatform.Modules.Guardian.Application.Commands;

/// <summary>Registers a new guardian.</summary>
/// <param name="Email">E-mail address of the guardian.</param>
/// <param name="FullName">Full name of the guardian; must not be blank.</param>
/// <param name="Phone">Optional phone number of the guardian.</param>
public sealed record CreateGuardianCommand(
    string Email,
    string FullName,
    string? Phone = null);

/// <summary>Links a guardian to a candidate; the link starts out pending verification.</summary>
/// <param name="GuardianId">The guardian to link.</param>
/// <param name="CandidateId">The candidate the guardian is linked to.</param>
/// <param name="CandidateEmail">E-mail address of the candidate.</param>
public sealed record LinkCandidateCommand(
    Guid GuardianId,
    Guid CandidateId,
    string CandidateEmail);

/// <summary>Revokes a guardian link to a candidate.</summary>
/// <param name="GuardianId">The guardian whose link is revoked.</param>
/// <param name="CandidateId">The candidate the link points to.</param>
public sealed record RevokeGuardianLinkCommand(
    Guid GuardianId,
    Guid CandidateId);

/// <summary>Unlinks (soft-deletes) a guardian-candidate link.</summary>
/// <param name="GuardianId">The guardian to unlink.</param>
/// <param name="CandidateId">The candidate to unlink.</param>
public sealed record UnlinkCandidateCommand(
    Guid GuardianId,
    Guid CandidateId);
