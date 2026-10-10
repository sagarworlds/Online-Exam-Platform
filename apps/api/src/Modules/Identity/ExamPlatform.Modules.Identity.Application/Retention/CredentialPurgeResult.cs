namespace ExamPlatform.Modules.Identity.Application.Retention;

/// <summary>How many expired credentials one sweep removed, by kind. Counts only: nothing here names a person.</summary>
/// <param name="OneTimeCodes">Sign-in and registration codes (<c>OtpChallenges</c>).</param>
/// <param name="PasswordResetTokens">Password-reset links (<c>PasswordResetTokens</c>).</param>
/// <param name="Sessions">Signed-in sessions (<c>UserSessions</c>).</param>
public sealed record CredentialPurgeResult(int OneTimeCodes, int PasswordResetTokens, int Sessions)
{
    /// <summary>Whether the sweep removed nothing at all.</summary>
    public bool IsEmpty => OneTimeCodes == 0 && PasswordResetTokens == 0 && Sessions == 0;
}
