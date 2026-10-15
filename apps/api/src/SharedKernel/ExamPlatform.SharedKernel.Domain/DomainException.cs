namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// Base type for typed domain errors (e.g. <c>OtpExpiredError</c>, <c>ConsentRequiredError</c>).
/// A global exception handler at the API boundary maps subclasses of this to
/// structured, actionable HTTP error responses — callers should never need a
/// bare <c>catch (Exception)</c> to handle a known domain failure.
/// </summary>
public abstract class DomainException : Exception
{
    private static readonly IReadOnlyDictionary<string, object?> NoExtensions = new Dictionary<string, object?>();

    /// <summary>A short, stable, machine-readable code identifying this error kind (e.g. "otp_expired").</summary>
    public abstract string ErrorCode { get; }

    /// <summary>
    /// Extra machine-readable facts the API boundary adds to the error response, for an error whose caller can act on more than its code
    /// (for example, the sequence number the server already holds, so a client can send a newer one). None by default. Values must be
    /// safe to show to the caller, and a key must not be one the boundary sets itself (such as <c>traceId</c>).
    /// </summary>
    public virtual IReadOnlyDictionary<string, object?> Extensions => NoExtensions;

    /// <summary>
    /// The HTTP status code the API boundary should map this error to. Declared
    /// here, per-exception, rather than in a central switch at the Host, so a new
    /// module's errors get sensible status codes without editing shared code
    /// (Open/Closed Principle). Defaults to 400 (Bad Request) — a plain int, not
    /// <c>Microsoft.AspNetCore.Http.StatusCodes</c>, since Domain must not depend
    /// on ASP.NET Core.
    /// </summary>
    public virtual int HttpStatusCode => 400;

    /// <summary>Initializes the exception with a human-readable message.</summary>
    /// <param name="message">Describes what went wrong, safe to surface to a caller.</param>
    protected DomainException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes the exception with a human-readable message and the lower-level
    /// failure that caused it, for errors translated from an infrastructure exception
    /// (e.g. a database concurrency conflict), so the original cause is not lost.
    /// </summary>
    /// <param name="message">Describes what went wrong, safe to surface to a caller.</param>
    /// <param name="innerException">The exception that caused this error, if any.</param>
    protected DomainException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
