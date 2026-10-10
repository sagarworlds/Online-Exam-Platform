using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Proctoring.Domain.Exceptions;

/// <summary>The review queue was asked for a filter that does not exist (400).</summary>
/// <param name="filterText">The text sent.</param>
public sealed class InvalidRiskFilterError(string filterText)
    : DomainException($"'{filterText}' is not a review filter. Use open, reviewed, dismissed or all.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_risk_filter";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
