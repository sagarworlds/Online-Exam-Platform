using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain.Exceptions;

/// <summary>
/// An incident or a status change breaks one of the log's rules: a required field is missing or too long, the category or status
/// is unknown, the detection time is not a UTC instant, or it lies in the future (400).
/// </summary>
/// <param name="message">What is wrong, in terms staff can act on. It names a field, never the text the field holds.</param>
public sealed class InvalidIncidentError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_incident";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
