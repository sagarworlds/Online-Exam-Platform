namespace ExamPlatform.Modules.Identity.Endpoints.RateLimiting;

/// <summary>
/// Names of the rate-limit policies the Identity module registers (NFR-5). The Host only
/// owns the global limiter; each policy here is added by <see cref="IdentityModuleInstaller"/>
/// and applied to its endpoints with <c>RequireRateLimiting</c>, so the Host never has to
/// know which routes are sensitive (Open/Closed, ADR 0001). Every policy partitions by
/// client IP and is tuned through <see cref="IdentityRateLimitOptions"/>.
/// </summary>
public static class IdentityRateLimitPolicies
{
    /// <summary>
    /// Anything that makes the API send a one-time code: <c>/otp/request</c> and
    /// <c>/register</c>. Limits both OTP flooding and SMS pumping.
    /// </summary>
    public const string OtpRequest = "identity-otp-request";

    /// <summary>Guessing a one-time code at <c>/otp/verify</c>.</summary>
    public const string OtpVerify = "identity-otp-verify";

    /// <summary>Guessing a password at <c>/login</c>.</summary>
    public const string PasswordLogin = "identity-password-login";

    /// <summary>Requesting or redeeming a password-reset link.</summary>
    public const string PasswordReset = "identity-password-reset";
}
