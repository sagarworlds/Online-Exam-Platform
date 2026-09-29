namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Completes a password reset using the token issued by <see cref="RequestPasswordResetCommand"/>.</summary>
/// <param name="PasswordResetTokenId">The token's id, from the reset link.</param>
/// <param name="Token">The raw token secret, from the reset link.</param>
/// <param name="NewPassword">The new plaintext password to set.</param>
public sealed record ResetPasswordCommand(Guid PasswordResetTokenId, string Token, string NewPassword);
