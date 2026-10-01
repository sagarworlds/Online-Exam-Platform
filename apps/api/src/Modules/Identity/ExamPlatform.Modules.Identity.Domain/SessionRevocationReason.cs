namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>Why a <see cref="UserSession"/> was revoked.</summary>
public enum SessionRevocationReason
{
    /// <summary>A newer login superseded this session (FR-4: one active session per candidate).</summary>
    SupersededByNewLogin,

    /// <summary>The user explicitly logged out.</summary>
    LoggedOut,

    /// <summary>An administrator forcibly ended the session.</summary>
    AdminForced,

    /// <summary>The account was suspended, which ends every session it had (FR-3).</summary>
    AccountSuspended,

    /// <summary>The account's password was reset, which ends every session started with the old one (FR-3).</summary>
    PasswordReset
}
