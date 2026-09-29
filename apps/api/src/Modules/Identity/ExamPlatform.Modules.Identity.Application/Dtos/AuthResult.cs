namespace ExamPlatform.Modules.Identity.Application.Dtos;

/// <summary>
/// Result of an authentication step. Either a completed login (<see cref="AccessToken"/>
/// and <see cref="SessionId"/> set) or a pending second factor (<see cref="OtpChallengeId"/>
/// set instead) — a staff/admin role with <c>RequiresTwoFactor</c> gets the latter from
/// password login, then completes it via the same OTP-verify endpoint used for candidate login.
/// </summary>
/// <param name="RequiresTwoFactor">Whether a second factor must still be completed.</param>
/// <param name="AccessToken">The signed bearer token, when login is complete.</param>
/// <param name="SessionId">The new session's id, when login is complete.</param>
/// <param name="OtpChallengeId">The challenge to verify next, when a second factor is pending.</param>
public sealed record AuthResult(
    bool RequiresTwoFactor,
    string? AccessToken,
    Guid? SessionId,
    Guid? OtpChallengeId)
{
    /// <summary>Builds a completed-login result.</summary>
    /// <param name="accessToken">The signed bearer token.</param>
    /// <param name="sessionId">The new session's id.</param>
    public static AuthResult Completed(string accessToken, Guid sessionId) =>
        new(false, accessToken, sessionId, null);

    /// <summary>Builds a pending-second-factor result.</summary>
    /// <param name="otpChallengeId">The challenge the caller must now verify.</param>
    public static AuthResult TwoFactorRequired(Guid otpChallengeId) =>
        new(true, null, null, otpChallengeId);
}
