using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>No dispute has that id.</summary>
public sealed class DisputeNotFoundError() : DomainException("No dispute matches the given id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "dispute_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
