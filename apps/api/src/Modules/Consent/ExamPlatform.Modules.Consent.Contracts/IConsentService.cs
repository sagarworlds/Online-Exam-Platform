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

    /// <summary>Reads the current consent status for a subject and purpose.</summary>
    /// <param name="subjectId">Whose consent to check.</param>
    /// <param name="purpose">What the consent must cover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConsentStatusDto> GetStatusAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken);

    /// <summary>Records a new consent grant.</summary>
    /// <param name="request">The subject, purpose, notice version, and who is giving consent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ConsentRecordDto> RecordConsentAsync(RecordConsentRequest request, CancellationToken cancellationToken);

    /// <summary>Withdraws a previously granted consent.</summary>
    /// <param name="consentRecordId">The consent record to withdraw.</param>
    /// <param name="withdrawnById">Who is withdrawing it (the subject or their guardian).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WithdrawConsentAsync(Guid consentRecordId, Guid withdrawnById, CancellationToken cancellationToken);

    /// <summary>Asserts that active consent exists, for a caller that only needs a pass/fail gate.</summary>
    /// <param name="subjectId">Whose consent to check.</param>
    /// <param name="purpose">What the consent must cover.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ConsentRequiredError">No active consent exists for this subject and purpose.</exception>
    Task EnsureConsentAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken);
}
