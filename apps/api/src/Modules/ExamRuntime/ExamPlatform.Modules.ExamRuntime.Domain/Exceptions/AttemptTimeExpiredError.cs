using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The attempt's deadline has passed, so answers are no longer accepted.</summary>
public sealed class AttemptTimeExpiredError() : DomainException("Time is up for this attempt.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_time_expired";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
