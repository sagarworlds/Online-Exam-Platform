using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt's typed answers are still being checked, so it has no result to review or change yet.</summary>
public sealed class AttemptBeingGradedError() : DomainException("This attempt's typed answers are still being checked. Try again once its result is ready.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_being_graded";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
