namespace ExamPlatform.Modules.Consent.Contracts;

/// <summary>
/// What a consent record or notice covers, as seen by other modules. Mirrors
/// <c>ExamPlatform.Modules.Consent.Domain.ConsentPurpose</c> — kept as a
/// separate type in <c>Contracts</c> so callers never take a dependency on
/// Consent's Domain assembly (module boundary rule, ADR 0001).
/// </summary>
public enum ConsentPurpose
{
    /// <summary>The platform's terms of service.</summary>
    TermsOfService,

    /// <summary>The DPDP-aligned privacy notice (exam-platform-requirements.md section 7).</summary>
    PrivacyNotice,

    /// <summary>Processing of proctoring data (camera/screen capture) for a specific exam profile.</summary>
    ProctoringDataProcessing
}
