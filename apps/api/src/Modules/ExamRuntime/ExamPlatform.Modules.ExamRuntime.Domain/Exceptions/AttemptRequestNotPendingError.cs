using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The request was already approved or declined, so it cannot be decided again.</summary>
public sealed class AttemptRequestNotPendingError() : DomainException("This request has already been decided.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_request_not_pending";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
