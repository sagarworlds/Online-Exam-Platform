namespace ExamPlatform.Modules.Identity.Application.Sessions;

/// <summary>
/// The stable error code and user-facing message for each rejected
/// <see cref="SessionValidationResult"/>. The code is sent as the 401 problem's title, which
/// the web app reads to explain why the user was signed out, so a code must never change.
/// </summary>
public static class SessionValidationResultErrors
{
    /// <summary>Returns the stable error code for a rejected session.</summary>
    /// <param name="result">A rejection; anything but <see cref="SessionValidationResult.Valid"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="result"/> is <see cref="SessionValidationResult.Valid"/>, which is not a rejection,
    /// or is not a defined result.
    /// </exception>
    public static string ToErrorCode(this SessionValidationResult result) => result switch
    {
        SessionValidationResult.Unknown => "session_unknown",
        SessionValidationResult.Superseded => "session_superseded",
        SessionValidationResult.Revoked => "session_revoked",
        SessionValidationResult.Expired => "session_expired",
        SessionValidationResult.AccountLocked => "account_locked",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Only a rejected session has an error code."),
    };

    /// <summary>Returns a message telling the user what happened and what to do next.</summary>
    /// <param name="result">A rejection; anything but <see cref="SessionValidationResult.Valid"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="result"/> is <see cref="SessionValidationResult.Valid"/>, which is not a rejection,
    /// or is not a defined result.
    /// </exception>
    public static string ToMessage(this SessionValidationResult result) => result switch
    {
        SessionValidationResult.Unknown => "This sign-in is not recognised. Please sign in again.",
        SessionValidationResult.Superseded =>
            "You were signed out because your account signed in somewhere else. Please sign in again.",
        SessionValidationResult.Revoked => "This sign-in has ended. Please sign in again.",
        SessionValidationResult.Expired => "Your sign-in has expired. Please sign in again.",
        SessionValidationResult.AccountLocked => "This account cannot currently sign in. Please contact support.",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Only a rejected session has a message."),
    };
}
