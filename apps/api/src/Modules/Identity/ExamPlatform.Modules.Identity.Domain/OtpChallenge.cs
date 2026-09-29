using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>
/// A one-time-password challenge: a code sent to an email or phone that the
/// caller must echo back before an expiry, with a bounded number of attempts.
/// Independent of <see cref="User"/> (not a child entity) because a challenge
/// can exist before any account does — e.g. the first OTP of a registration.
/// </summary>
public sealed class OtpChallenge : AggregateRoot
{
    /// <summary>The user this challenge is for, if one already exists (absent for a first-time registration OTP).</summary>
    public Guid? UserId { get; private set; }

    /// <summary>How the code was delivered.</summary>
    public OtpChannel Channel { get; private set; }

    /// <summary>The email address or phone number the code was sent to.</summary>
    public string Destination { get; private set; }

    /// <summary>Hash of the code, never the raw code.</summary>
    public string CodeHash { get; private set; }

    /// <summary>What this challenge authorizes.</summary>
    public OtpPurpose Purpose { get; private set; }

    /// <summary>When the code stops being acceptable.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>When the challenge was successfully verified, if it has been.</summary>
    public DateTime? ConsumedAtUtc { get; private set; }

    /// <summary>How many verification attempts have been made so far.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>The maximum number of incorrect attempts allowed before the challenge is locked out.</summary>
    public int MaxAttempts { get; private set; }

    private OtpChallenge(
        Guid id,
        Guid? userId,
        OtpChannel channel,
        string destination,
        string codeHash,
        OtpPurpose purpose,
        DateTime expiresAtUtc,
        int maxAttempts) : base(id)
    {
        UserId = userId;
        Channel = channel;
        Destination = destination;
        CodeHash = codeHash;
        Purpose = purpose;
        ExpiresAtUtc = expiresAtUtc;
        MaxAttempts = maxAttempts;
    }

    /// <summary>Issues a new OTP challenge.</summary>
    /// <param name="userId">The existing user this is for, or null for a pre-registration challenge.</param>
    /// <param name="channel">How the code is being delivered.</param>
    /// <param name="destination">The email address or phone number the code is sent to.</param>
    /// <param name="codeHash">Hash of the generated code.</param>
    /// <param name="purpose">What this challenge authorizes.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="validity">How long the code remains acceptable.</param>
    /// <param name="maxAttempts">How many incorrect attempts are allowed before lockout.</param>
    public static OtpChallenge Issue(
        Guid? userId,
        OtpChannel channel,
        string destination,
        string codeHash,
        OtpPurpose purpose,
        DateTime nowUtc,
        TimeSpan validity,
        int maxAttempts = 5) =>
        new(Guid.NewGuid(), userId, channel, destination, codeHash, purpose, nowUtc.Add(validity), maxAttempts);

    /// <summary>
    /// Checks a supplied code against this challenge. Never returns a bare
    /// <c>false</c> for a failure — each failure mode is a distinct, typed
    /// exception so a caller can react appropriately (e.g. "expired" prompts a
    /// resend; "mismatch" lets the user retry; "attempts exceeded" forces a resend).
    /// </summary>
    /// <param name="suppliedCodeHash">Hash of the code the caller supplied, computed the same way as <see cref="CodeHash"/>.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="OtpAttemptsExceededError">The challenge has already reached <see cref="MaxAttempts"/> incorrect tries.</exception>
    /// <exception cref="OtpExpiredError">The challenge was checked after <see cref="ExpiresAtUtc"/>.</exception>
    /// <exception cref="OtpMismatchError">The supplied code does not match.</exception>
    public void Verify(string suppliedCodeHash, DateTime nowUtc)
    {
        if (AttemptCount >= MaxAttempts)
        {
            throw new OtpAttemptsExceededError();
        }

        if (nowUtc > ExpiresAtUtc)
        {
            throw new OtpExpiredError();
        }

        AttemptCount++;

        if (!string.Equals(suppliedCodeHash, CodeHash, StringComparison.Ordinal))
        {
            throw new OtpMismatchError();
        }

        ConsumedAtUtc = nowUtc;
    }

    /// <summary>Whether this challenge has already been successfully verified.</summary>
    public bool IsConsumed => ConsumedAtUtc is not null;
}
