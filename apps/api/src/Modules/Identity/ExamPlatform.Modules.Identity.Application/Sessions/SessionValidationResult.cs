namespace ExamPlatform.Modules.Identity.Application.Sessions;

/// <summary>Whether an access token's session may still be used, and if not, why (FR-4).</summary>
public enum SessionValidationResult
{
    /// <summary>The session is live and its account may use it.</summary>
    Valid,

    /// <summary>No session matches the token's <c>sid</c>, or it belongs to a different user than the token's <c>sub</c>.</summary>
    Unknown,

    /// <summary>A newer login ended the session (FR-4: one active session per candidate).</summary>
    Superseded,

    /// <summary>The session was ended for another reason, such as logging out or a password reset.</summary>
    Revoked,

    /// <summary>The session reached its natural expiry.</summary>
    Expired,

    /// <summary>The account the session belongs to is suspended or deactivated.</summary>
    AccountLocked
}
