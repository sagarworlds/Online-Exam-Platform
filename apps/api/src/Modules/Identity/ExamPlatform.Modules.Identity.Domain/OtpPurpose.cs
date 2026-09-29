namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>What a one-time-password challenge is being used to authorize.</summary>
public enum OtpPurpose
{
    /// <summary>A candidate logging in (FR-1: email/phone OTP login).</summary>
    Login,

    /// <summary>Confirming a new account during registration.</summary>
    Registration,

    /// <summary>Authorizing a password reset.</summary>
    PasswordReset,

    /// <summary>The second factor for a staff/admin role with mandatory 2FA (FR-3).</summary>
    TwoFactorStep
}
