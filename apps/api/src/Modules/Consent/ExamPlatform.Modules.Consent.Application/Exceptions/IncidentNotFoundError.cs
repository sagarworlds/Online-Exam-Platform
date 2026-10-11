using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Application.Exceptions;

/// <summary>No incident matches the id supplied (404).</summary>
public sealed class IncidentNotFoundError() : DomainException("No matching incident was found.")
{
    /// <inheritdoc />
    public override string ErrorCode => "incident_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
