using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt is paused by an administrator, so the candidate cannot change it until it is resumed (FR-29).</summary>
public sealed class AttemptPausedError() : DomainException("This attempt is paused by an administrator. Wait for it to be resumed.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_paused";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
