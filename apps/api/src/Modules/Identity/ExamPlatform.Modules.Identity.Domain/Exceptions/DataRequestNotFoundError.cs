using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>No data request has the id that was given.</summary>
public sealed class DataRequestNotFoundError() : DomainException("No data request has that id.")
{
    /// <inheritdoc />
    public override string ErrorCode => "data_request_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
