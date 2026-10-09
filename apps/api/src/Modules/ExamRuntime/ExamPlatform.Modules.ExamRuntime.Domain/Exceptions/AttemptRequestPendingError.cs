using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The candidate already has a pending request for this exam.</summary>
public sealed class AttemptRequestPendingError() : DomainException("You already have a request for this exam waiting for an administrator.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_request_pending";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
