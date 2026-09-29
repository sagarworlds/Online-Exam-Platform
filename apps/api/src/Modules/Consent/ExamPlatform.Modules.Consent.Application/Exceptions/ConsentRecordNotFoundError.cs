using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Application.Exceptions;

/// <summary>No consent record matches the id supplied.</summary>
public sealed class ConsentRecordNotFoundError() : DomainException("No matching consent record was found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "consent_record_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
