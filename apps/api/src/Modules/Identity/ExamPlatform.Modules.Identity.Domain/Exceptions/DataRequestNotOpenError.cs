using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>The request was already answered, so it cannot be answered again.</summary>
public sealed class DataRequestNotOpenError() : DomainException("This request has already been answered.")
{
    /// <inheritdoc />
    public override string ErrorCode => "data_request_not_open";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
