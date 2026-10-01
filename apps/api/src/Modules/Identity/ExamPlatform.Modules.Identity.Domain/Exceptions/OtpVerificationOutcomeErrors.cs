using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>Maps a failed <see cref="OtpVerificationOutcome"/> to the typed error the API reports for it.</summary>
public static class OtpVerificationOutcomeErrors
{
    /// <summary>Returns the typed error for a failed verification, for the caller to throw.</summary>
    /// <param name="outcome">A failure outcome; anything but <see cref="OtpVerificationOutcome.Verified"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="outcome"/> is <see cref="OtpVerificationOutcome.Verified"/>, which is not a failure,
    /// or is not a defined outcome.
    /// </exception>
    public static DomainException ToError(this OtpVerificationOutcome outcome) => outcome switch
    {
        OtpVerificationOutcome.Mismatch => new OtpMismatchError(),
        OtpVerificationOutcome.Expired => new OtpExpiredError(),
        OtpVerificationOutcome.AttemptsExceeded => new OtpAttemptsExceededError(),
        OtpVerificationOutcome.AlreadyUsed => new OtpAlreadyUsedError(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Only a failed OTP verification outcome maps to an error."),
    };
}
