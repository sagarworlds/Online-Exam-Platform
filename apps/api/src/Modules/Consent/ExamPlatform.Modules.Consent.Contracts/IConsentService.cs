namespace ExamPlatform.Modules.Consent.Contracts;

/// <summary>
/// Runtime-facing gate: "may this candidate proceed?" (exam-platform-requirements.md
/// section 11). Consumed by other modules — most notably the future Exam Runtime,
/// which must refuse to start an attempt for a minor without guardian consent —
/// via this Contracts project only, never via Consent's Domain, Application, or
/// Infrastructure, preserving the module boundary (DIP).
/// </summary>
public interface IConsentService
{
    /// <summary>Whether the subject currently has active (not withdrawn) consent for the purpose.</summary>
    /// <param name="subjectId">Whose consent to check.</param>
    /// <param name="purpose">What the consent must cover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasActiveConsentAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken);

    /// <summary>Reads the current consent status for a subject and purpose. Only the subject may read their own status.</summary>
    /// <param name="subjectId">Whose consent to check.</param>
    /// <param name="purpose">What the consent must cover.</param>
    /// <param name="requestedById">Who is asking; must be the subject.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ConsentAccessDeniedError">The requester is not the subject.</exception>
    Task<ConsentStatusDto> GetStatusAsync(Guid subjectId, ConsentPurpose purpose, Guid requestedById, CancellationToken cancellationToken);

    /// <summary>Records a new consent grant. A person may only grant their own consent.</summary>
    /// <param name="request">The subject, purpose, notice version, and who is giving consent; the two people must be the same.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ConsentAccessDeniedError">The person giving consent is not the subject.</exception>
    Task<ConsentRecordDto> RecordConsentAsync(RecordConsentRequest request, CancellationToken cancellationToken);

    /// <summary>Withdraws a previously granted consent. Only the subject may withdraw it.</summary>
    /// <param name="consentRecordId">The consent record to withdraw.</param>
    /// <param name="withdrawnById">Who is withdrawing it; must be the subject. Another person's record is reported as not found.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ConsentRecordNotFoundError">No record with that id belongs to the requester.</exception>
    Task WithdrawConsentAsync(Guid consentRecordId, Guid withdrawnById, CancellationToken cancellationToken);

    /// <summary>Asserts that active consent exists, for a caller that only needs a pass/fail gate.</summary>
    /// <param name="subjectId">Whose consent to check.</param>
    /// <param name="purpose">What the consent must cover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ConsentRequiredError">No active consent exists for this subject and purpose.</exception>
    Task EnsureConsentAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken);
}
