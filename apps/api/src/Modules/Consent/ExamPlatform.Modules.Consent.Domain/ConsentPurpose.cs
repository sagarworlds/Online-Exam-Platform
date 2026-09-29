namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// What a consent record or notice covers. A closed set for this slice; adding a
/// new purpose is a matter of adding a value here plus a seeded <see cref="NoticeVersion"/> —
/// no schema change (Open/Closed Principle).
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
