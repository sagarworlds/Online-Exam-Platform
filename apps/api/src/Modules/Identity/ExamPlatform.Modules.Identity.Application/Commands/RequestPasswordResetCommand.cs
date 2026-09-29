namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Requests a password reset link for a staff/admin account (FR-3).</summary>
/// <param name="Email">The account's email address.</param>
public sealed record RequestPasswordResetCommand(string Email);
