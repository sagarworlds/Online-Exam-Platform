using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt does not exist, or belongs to someone else (the two are not told apart).</summary>
public sealed class AttemptNotFoundError() : DomainException("No attempt matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
