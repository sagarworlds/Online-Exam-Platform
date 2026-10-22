using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>A result was given to an attempt that is not waiting for one.</summary>
public sealed class AttemptNotGradingError() : DomainException("This attempt is not waiting for its typed answers to be checked.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_not_grading";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
