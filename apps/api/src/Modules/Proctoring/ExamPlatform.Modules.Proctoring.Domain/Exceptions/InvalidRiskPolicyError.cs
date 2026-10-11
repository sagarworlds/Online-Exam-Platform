using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Proctoring.Domain.Exceptions;

/// <summary>The configured risk score is not usable. A host with this configuration does not start (500 if it ever reaches a caller).</summary>
/// <param name="problem">What is wrong with the configuration, in words an operator can act on.</param>
public sealed class InvalidRiskPolicyError(string problem) : DomainException(problem)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_risk_policy";

    /// <inheritdoc />
    public override int HttpStatusCode => 500;
}
