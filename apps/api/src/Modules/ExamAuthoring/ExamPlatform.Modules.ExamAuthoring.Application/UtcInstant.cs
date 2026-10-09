namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>Reads an instant a caller sent as UTC, which is the API's contract for every time it accepts.</summary>
internal static class UtcInstant
{
    /// <summary>
    /// A time sent without an offset arrives with an unspecified kind; it is read as UTC rather than guessed to be
    /// server-local time, and one sent with an offset is converted.
    /// </summary>
    /// <param name="value">The instant as bound from the request.</param>
    public static DateTime From(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}
