namespace ExamPlatform.SharedKernel.Domain.Exceptions;

/// <summary>
/// THE shared 409 for optimistic-concurrency conflicts: another request changed the
/// same row (detected through its Postgres <c>xmin</c> row version) between this
/// request loading it and saving it, so this request's write was rejected rather
/// than silently overwriting the other one. A module's unit of work translates its
/// ORM's concurrency exception into this error, so every module answers a lost-update
/// race the same way. Later modules reuse it as is, or subclass it when a
/// conflict needs a more specific message or error code.
/// </summary>
public class ConcurrencyConflictError : DomainException
{
    /// <summary>Initializes the error with the default, generic conflict message.</summary>
    /// <param name="innerException">The persistence-layer exception that detected the conflict, if any.</param>
    public ConcurrencyConflictError(Exception? innerException = null)
        : this("This record was changed by another request at the same time; reload it and try again.", innerException)
    {
    }

    /// <summary>Initializes the error with a conflict message specific to a subclass.</summary>
    /// <param name="message">Describes the conflict, safe to surface to a caller.</param>
    /// <param name="innerException">The persistence-layer exception that detected the conflict, if any.</param>
    protected ConcurrencyConflictError(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <inheritdoc />
    public override string ErrorCode => "concurrency_conflict";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
