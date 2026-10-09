using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt is already submitted, so it can no longer change.</summary>
public sealed class AttemptNotInProgressError() : DomainException("This attempt has already been submitted.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_not_in_progress";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
