namespace ExamPlatform.Modules.Invite.Application.Commands;

/// <summary>Invites an e-mail address to an exam and e-mails it a link.</summary>
/// <param name="ExamId">The exam the address is invited to; it must exist.</param>
/// <param name="BatchMemberId">The roster entry it came from, if any.</param>
/// <param name="Email">The invited address.</param>
/// <param name="CreatedByUserId">The staff user inviting.</param>
/// <param name="DirectSignIn">
/// Whether the link sent to the invited person signs them in as well as accepting the invitation. The caller has already checked that
/// whoever asked may make such a link.
/// </param>
public sealed record CreateInviteCommand(Guid ExamId, Guid? BatchMemberId, string Email, Guid CreatedByUserId, bool DirectSignIn = false);

/// <summary>Adds another code to an invite.</summary>
/// <param name="InviteId">The invite.</param>
/// <param name="ExpiryHours">How long the code stays valid, in hours.</param>
/// <param name="DirectSignIn">Whether the code's link signs the invited person in; the caller has already checked that whoever asked may make one.</param>
public sealed record GenerateInviteCodeCommand(Guid InviteId, int ExpiryHours = 72, bool DirectSignIn = false);

/// <summary>Redeems an invite code for the signed-in user.</summary>
/// <param name="Code">The code from the invitation link.</param>
/// <param name="UserId">The accepting account.</param>
/// <param name="Email">The accepting account's verified e-mail address, if it has one.</param>
public sealed record AcceptInviteCommand(string? Code, Guid UserId, string? Email);

/// <summary>Opens a direct sign-in link: accepts the invitation and signs the invited person in.</summary>
/// <param name="Code">The code from the link.</param>
/// <param name="DeviceFingerprint">The device signature the web app sent, if any, recorded on the new session.</param>
/// <param name="IpAddress">The caller's IP address as the server sees it, if it could be read, recorded on the new session.</param>
public sealed record DirectSignInCommand(string? Code, string? DeviceFingerprint, string? IpAddress);

/// <summary>Declines an invite.</summary>
/// <param name="InviteId">The invite to decline.</param>
/// <param name="Email">The declining account's verified e-mail address, if it has one; it must be the invited address.</param>
public sealed record DeclineInviteCommand(Guid InviteId, string? Email);

/// <summary>Revokes an invite and all of its codes.</summary>
/// <param name="InviteId">The invite to revoke.</param>
public sealed record RevokeInviteCommand(Guid InviteId);

/// <summary>Changes the address on every invitation sent to one address, so a student's invitations stay together under their new address.</summary>
/// <param name="CurrentEmail">The address the invitations were sent to.</param>
/// <param name="NewEmail">The address they are to have.</param>
public sealed record ChangeInviteEmailCommand(string? CurrentEmail, string? NewEmail);
