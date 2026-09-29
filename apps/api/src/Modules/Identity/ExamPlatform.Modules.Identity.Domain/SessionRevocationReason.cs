namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>Why a <see cref="UserSession"/> was revoked.</summary>
public enum SessionRevocationReason
{
    /// <summary>A newer login superseded this session (FR-4: one active session per candidate).</summary>
    SupersededByNewLogin,

    /// <summary>The user explicitly logged out.</summary>
    LoggedOut,

    /// <summary>An administrator forcibly ended the session.</summary>
    AdminForced
}
