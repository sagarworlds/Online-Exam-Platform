namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// Base type for typed domain errors (e.g. <c>OtpExpiredError</c>, <c>ConsentRequiredError</c>).
/// A global exception handler at the API boundary maps subclasses of this to
/// structured, actionable HTTP error responses — callers should never need a
/// bare <c>catch (Exception)</c> to handle a known domain failure.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>A short, stable, machine-readable code identifying this error kind (e.g. "otp_expired").</summary>
    public abstract string ErrorCode { get; }

    /// <summary>Initializes the exception with a human-readable message.</summary>
    /// <param name="message">Describes what went wrong, safe to surface to a caller.</param>
    protected DomainException(string message) : base(message)
    {
    }
}
