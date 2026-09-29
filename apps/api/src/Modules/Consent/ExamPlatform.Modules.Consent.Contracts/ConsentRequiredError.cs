using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Contracts;

/// <summary>
/// No active consent exists for the given subject and purpose. The doc's own
/// named typed error (exam-platform-requirements.md section 11) — a future
/// caller (e.g. Exam Runtime, gating attempt start) catches this without
/// referencing anything from Consent beyond this Contracts project.
/// </summary>
public sealed class ConsentRequiredError() : DomainException("Active consent is required before proceeding.")
{
    /// <inheritdoc />
    public override string ErrorCode => "consent_required";
}
