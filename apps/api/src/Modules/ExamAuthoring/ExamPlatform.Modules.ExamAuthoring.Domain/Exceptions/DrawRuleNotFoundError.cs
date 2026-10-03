using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

/// <summary>The section has no draw rule with the requested id.</summary>
public sealed class DrawRuleNotFoundError(Guid ruleId, Guid sectionId) : DomainException($"Draw rule {ruleId} was not found in section {sectionId}.")
{
    /// <inheritdoc />
    public override string ErrorCode => "draw_rule_not_found";

    /// <inheritdoc />
    public override int HttpStatusCode => 404;
}
