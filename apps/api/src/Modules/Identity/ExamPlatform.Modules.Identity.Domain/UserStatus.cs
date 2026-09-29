namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>Lifecycle state of a <see cref="User"/> account.</summary>
public enum UserStatus
{
    /// <summary>Registered but not yet confirmed via OTP or (for minors) guardian consent.</summary>
    PendingVerification,

    /// <summary>Verified and able to log in.</summary>
    Active,

    /// <summary>Temporarily blocked by an administrator; cannot log in.</summary>
    Suspended,

    /// <summary>Permanently deactivated; cannot log in. Retained for audit/history.</summary>
    Deactivated
}
