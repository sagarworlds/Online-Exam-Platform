namespace ExamPlatform.Modules.Invite.Application.Commands;

/// <summary>Invites an e-mail address to an exam and e-mails it a link.</summary>
/// <param name="ExamId">The exam the address is invited to; it must exist.</param>
/// <param name="BatchMemberId">The roster entry it came from, if any.</param>
/// <param name="Email">The invited address.</param>
/// <param name="CreatedByUserId">The staff user inviting.</param>
public sealed record CreateInviteCommand(Guid ExamId, Guid? BatchMemberId, string Email, Guid CreatedByUserId);

/// <summary>Adds another code to an invite.</summary>
/// <param name="InviteId">The invite.</param>
/// <param name="ExpiryHours">How long the code stays valid, in hours.</param>
public sealed record GenerateInviteCodeCommand(Guid InviteId, int ExpiryHours = 72);

/// <summary>Redeems an invite code for the signed-in user.</summary>
/// <param name="Code">The code from the invitation link.</param>
/// <param name="UserId">The accepting account.</param>
/// <param name="Email">The accepting account's verified e-mail address, if it has one.</param>
public sealed record AcceptInviteCommand(string? Code, Guid UserId, string? Email);

/// <summary>Declines an invite.</summary>
/// <param name="InviteId">The invite to decline.</param>
/// <param name="Email">The declining account's verified e-mail address, if it has one; it must be the invited address.</param>
public sealed record DeclineInviteCommand(Guid InviteId, string? Email);

/// <summary>Revokes an invite and all of its codes.</summary>
/// <param name="InviteId">The invite to revoke.</param>
public sealed record RevokeInviteCommand(Guid InviteId);
