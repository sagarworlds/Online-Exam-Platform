using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>No attempt request has that id.</summary>
public sealed class AttemptRequestNotFoundError() : DomainException("No request matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "attempt_request_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
