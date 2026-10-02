namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>
/// The result of checking a code against an <see cref="OtpChallenge"/>. Every value
/// except <see cref="Verified"/> is a failure the caller reports as its typed error
/// (see <see cref="Exceptions.OtpVerificationOutcomeErrors.ToError"/>) once any state the check changed
/// has been saved.
/// </summary>
public enum OtpVerificationOutcome
{
    /// <summary>The code matched; the challenge is now consumed.</summary>
    Verified,

    /// <summary>The code did not match; the attempt was counted.</summary>
    Mismatch,

    /// <summary>The challenge was checked after its expiry; no attempt was counted.</summary>
    Expired,

    /// <summary>The challenge has used up its allowed attempts; no further code is accepted.</summary>
    AttemptsExceeded,

    /// <summary>The challenge was already consumed by an earlier successful verify (a replay).</summary>
    AlreadyUsed,

    /// <summary>
    /// A newer challenge for the same destination and purpose replaced this one; only the
    /// most recent code is accepted, and no attempt was counted.
    /// </summary>
    Superseded
}
