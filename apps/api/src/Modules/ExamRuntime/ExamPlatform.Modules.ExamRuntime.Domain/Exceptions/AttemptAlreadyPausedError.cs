using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt is already paused.</summary>
public sealed class AttemptAlreadyPausedError() : DomainException("This attempt is already paused.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_already_paused";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
