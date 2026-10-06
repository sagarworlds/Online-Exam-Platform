using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt result was invalidated by an administrator, so it has no score or answer review to show (FR-29).</summary>
public sealed class AttemptInvalidatedError() : DomainException("This attempt was invalidated by an administrator, so it has no result to show.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_invalidated";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
