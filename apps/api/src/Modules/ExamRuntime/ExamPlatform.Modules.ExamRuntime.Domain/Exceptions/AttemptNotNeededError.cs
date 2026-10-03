using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The candidate still has an attempt left, or one in progress, so a request now would be premature.</summary>
public sealed class AttemptNotNeededError() : DomainException("You still have an attempt left, so there is nothing to ask for yet.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_not_needed";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
