using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt is not paused, so there is nothing to resume.</summary>
public sealed class AttemptNotPausedError() : DomainException("This attempt is not paused.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_not_paused";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
