namespace ExamPlatform.Modules.Invite.Endpoints.RateLimiting;

/// <summary>
/// Names of the rate-limit policies the Invite module registers (NFR-5). The Host owns only the global limiter; each policy here is added
/// by <see cref="InviteModuleInstaller"/> and applied to its route with <c>RequireRateLimiting</c>, so the Host never has to know which
/// routes are sensitive (ADR 0001). Every policy partitions by client IP and is tuned through <see cref="InviteRateLimitOptions"/>.
/// </summary>
public static class InviteRateLimitPolicies
{
    /// <summary>Opening a direct sign-in link at <c>/v1/invites/direct-sign-in</c>: guessing a code that signs someone in.</summary>
    public const string DirectSignIn = "invite-direct-sign-in";
}
