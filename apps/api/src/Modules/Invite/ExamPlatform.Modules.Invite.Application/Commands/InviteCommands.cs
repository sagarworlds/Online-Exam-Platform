namespace ExamPlatform.Modules.Invite.Application.Commands;

/// <summary>Creates a new invite for a batch member to sit an exam.</summary>
/// <param name="ExamId">The exam the candidate is invited to.</param>
/// <param name="BatchMemberId">The batch member being invited.</param>
/// <param name="Email">E-mail address the invite is sent to.</param>
/// <param name="CreatedByUserId">The user that creates the invite.</param>
public sealed record CreateInviteCommand(
    Guid ExamId,
    Guid BatchMemberId,
    string Email,
    Guid CreatedByUserId);

/// <summary>Generates an invite code for an existing invite.</summary>
/// <param name="InviteId">The invite to generate the code for.</param>
/// <param name="ExpiryHours">How long the code stays valid, in hours.</param>
public sealed record GenerateInviteCodeCommand(
    Guid InviteId,
    int ExpiryHours = 72);

/// <summary>Accepts an invite with one of its codes.</summary>
/// <param name="InviteId">The invite to accept.</param>
/// <param name="InviteCodeId">The code that is redeemed.</param>
public sealed record AcceptInviteCommand(
    Guid InviteId,
    Guid InviteCodeId);

/// <summary>Declines an invite.</summary>
/// <param name="InviteId">The invite to decline.</param>
public sealed record DeclineInviteCommand(Guid InviteId);

/// <summary>Revokes an invite and all of its codes.</summary>
/// <param name="InviteId">The invite to revoke.</param>
public sealed record RevokeInviteCommand(Guid InviteId);
