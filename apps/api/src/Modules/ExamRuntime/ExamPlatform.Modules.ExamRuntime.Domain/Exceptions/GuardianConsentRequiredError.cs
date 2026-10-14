using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>
/// A candidate under 18 cannot start an exam until a guardian has confirmed the link to them (exam-platform-requirements.md section 7, FR-43).
/// A resumed attempt is not refused by this, because the rule guards the start only.
/// </summary>
public sealed class GuardianConsentRequiredError() : DomainException("A candidate under 18 can start an exam only after a guardian has confirmed the link to them.")
{
    /// <inheritdoc />
    public override string ErrorCode => "guardian_consent_required";

    /// <inheritdoc />
    public override int HttpStatusCode => 403;
}
