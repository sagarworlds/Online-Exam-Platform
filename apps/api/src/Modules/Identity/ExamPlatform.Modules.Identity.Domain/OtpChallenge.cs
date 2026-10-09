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
    /// <summary>
    /// The longest destination a challenge can hold; long enough for the longest email address
    /// (<see cref="User.MaxEmailLength"/>), which is longer than any phone number.
    /// </summary>
    public const int MaxDestinationLength = User.MaxEmailLength;

    /// <summary>
    /// The user this challenge is for, if one already exists. Null for a challenge issued
    /// before any account exists, and for a decoy: a challenge issued to a destination that
    /// must not get a usable code, which is never sent and whose code can never match.
    /// </summary>
    public Guid? UserId { get; private set; }

    /// <summary>How the code was delivered.</summary>
    public OtpChannel Channel { get; private set; }

    /// <summary>The email address or phone number the code was sent to.</summary>
    public string Destination { get; private set; }

    /// <summary>Hash of the code, never the raw code.</summary>
    public string CodeHash { get; private set; }

    /// <summary>
    /// The plaintext code, kept only so an administrator can read it out to a candidate who
    /// cannot receive it (see <see cref="OtpPurposeExtensions.IsRevealableToStaff"/>). Null for
    /// purposes that must never be readable, for decoys, and once the challenge is consumed
    /// or superseded: the code lives only as long as it can still be used.
    /// </summary>
    public string? RevealableCode { get; private set; }

    /// <summary>What this challenge authorizes.</summary>
    public OtpPurpose Purpose { get; private set; }

    /// <summary>When the code stops being acceptable.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>When the challenge was successfully verified, if it has been.</summary>
    public DateTime? ConsumedAtUtc { get; private set; }

    /// <summary>
    /// When a newer challenge for the same destination and purpose replaced this one, if
    /// one has; a superseded challenge no longer accepts any code.
    /// </summary>
    public DateTime? SupersededAtUtc { get; private set; }

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
        int maxAttempts,
        string? revealableCode) : base(id)
    {
        UserId = userId;
        Channel = channel;
        Destination = destination;
        CodeHash = codeHash;
        Purpose = purpose;
        ExpiresAtUtc = expiresAtUtc;
        MaxAttempts = maxAttempts;
        RevealableCode = revealableCode;
    }

    /// <summary>Issues a new OTP challenge.</summary>
    /// <param name="userId">The existing user this is for, or null for a pre-registration or decoy challenge.</param>
    /// <param name="channel">How the code is being delivered.</param>
    /// <param name="destination">The email address or phone number the code is sent to.</param>
    /// <param name="codeHash">Hash of the generated code.</param>
    /// <param name="purpose">What this challenge authorizes.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <param name="validity">How long the code remains acceptable.</param>
    /// <param name="maxAttempts">How many incorrect attempts are allowed before lockout.</param>
    /// <param name="revealableCode">
    /// The plaintext code to keep for staff to read, or null to keep none. Ignored for a purpose
    /// that is not <see cref="OtpPurposeExtensions.IsRevealableToStaff">revealable</see>, so no
    /// caller can store a readable second factor by mistake.
    /// </param>
    public static OtpChallenge Issue(
        Guid? userId,
        OtpChannel channel,
        string destination,
        string codeHash,
        OtpPurpose purpose,
        DateTime nowUtc,
        TimeSpan validity,
        int maxAttempts = 5,
        string? revealableCode = null) =>
        new(Guid.NewGuid(), userId, channel, destination, codeHash, purpose, nowUtc.Add(validity), maxAttempts,
            purpose.IsRevealableToStaff() ? revealableCode : null);

    /// <summary>
    /// Checks a supplied code against this challenge and returns a structured
    /// outcome instead of throwing (requirements section 1.2 allows a structured
    /// error state): a wrong code changes state — it counts against
    /// <see cref="MaxAttempts"/> — and that state must be saved before the caller
    /// reports the failure, or the attempt is lost and the lockout never engages.
    /// The caller saves, then throws <see cref="OtpVerificationOutcomeErrors.ToError"/>
    /// for anything but <see cref="OtpVerificationOutcome.Verified"/>.
    /// <para>
    /// Checked in order: an already consumed challenge is a replay and is refused
    /// first; then a challenge a newer code has superseded; then an exhausted attempt
    /// budget; then expiry. Only a check that reaches the code comparison counts as
    /// an attempt.
    /// </para>
    /// </summary>
    /// <param name="suppliedCodeHash">Hash of the code the caller supplied, computed the same way as <see cref="CodeHash"/>.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>
    /// <see cref="OtpVerificationOutcome.Verified"/> (and the challenge is now consumed) when the code matches;
    /// otherwise the reason it was refused.
    /// </returns>
    public OtpVerificationOutcome Verify(string suppliedCodeHash, DateTime nowUtc)
    {
        if (IsConsumed)
        {
            return OtpVerificationOutcome.AlreadyUsed;
        }

        if (IsSuperseded)
        {
            return OtpVerificationOutcome.Superseded;
        }

        if (AttemptCount >= MaxAttempts)
        {
            return OtpVerificationOutcome.AttemptsExceeded;
        }

        if (nowUtc > ExpiresAtUtc)
        {
            return OtpVerificationOutcome.Expired;
        }

        AttemptCount++;

        if (!string.Equals(suppliedCodeHash, CodeHash, StringComparison.Ordinal))
        {
            return OtpVerificationOutcome.Mismatch;
        }

        ConsumedAtUtc = nowUtc;
        RevealableCode = null;
        return OtpVerificationOutcome.Verified;
    }

    /// <summary>
    /// Retires this challenge because a newer one has been issued for the same
    /// destination and purpose, so only the most recent code stays verifiable. A no-op
    /// for a challenge that is already consumed (its outcome is final) or already
    /// superseded (the first supersession time is kept).
    /// </summary>
    /// <param name="nowUtc">The current instant.</param>
    public void Supersede(DateTime nowUtc)
    {
        if (IsConsumed || IsSuperseded)
        {
            return;
        }

        SupersededAtUtc = nowUtc;
        RevealableCode = null;
    }

    /// <summary>Whether this challenge has already been successfully verified.</summary>
    public bool IsConsumed => ConsumedAtUtc is not null;

    /// <summary>Whether a newer challenge for the same destination and purpose has replaced this one.</summary>
    public bool IsSuperseded => SupersededAtUtc is not null;
}
