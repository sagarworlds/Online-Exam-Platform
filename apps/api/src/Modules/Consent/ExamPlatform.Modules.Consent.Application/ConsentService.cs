using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Application.Exceptions;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Contracts;
using ExamPlatform.SharedKernel.Application;
using Domain = ExamPlatform.Modules.Consent.Domain;

namespace ExamPlatform.Modules.Consent.Application;

/// <summary>
/// Implements the module's public contract (<see cref="Contracts.IConsentService"/>).
/// Lives in Application, not Infrastructure, because it is orchestration over
/// repositories and domain behaviour, not I/O plumbing itself.
/// </summary>
public sealed class ConsentService(
    IConsentRecordRepository consentRecordRepository,
    INoticeVersionRepository noticeVersionRepository,
    IAuditLogger auditLogger,
    IConsentUnitOfWork unitOfWork,
    Clock clock) : IConsentService
{
    /// <inheritdoc />
    public async Task<bool> HasActiveConsentAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken)
    {
        var record = await consentRecordRepository.GetActiveAsync(subjectId, ToDomain(purpose), cancellationToken);
        return record is not null;
    }

    /// <inheritdoc />
    public async Task<ConsentStatusDto> GetStatusAsync(Guid subjectId, ConsentPurpose purpose, Guid requestedById, CancellationToken cancellationToken)
    {
        EnsureOwnConsent(subjectId, requestedById);

        var domainPurpose = ToDomain(purpose);
        var record = await consentRecordRepository.GetActiveAsync(subjectId, domainPurpose, cancellationToken);
        var currentNotice = await noticeVersionRepository.GetCurrentAsync(domainPurpose, cancellationToken);
        return new ConsentStatusDto(subjectId, purpose, record is not null, record?.Id, currentNotice?.Id);
    }

    /// <inheritdoc />
    public async Task<ConsentRecordDto> RecordConsentAsync(RecordConsentRequest request, CancellationToken cancellationToken)
    {
        EnsureOwnConsent(request.SubjectId, request.GivenById);

        _ = await noticeVersionRepository.GetByIdAsync(request.NoticeVersionId, cancellationToken)
            ?? throw new NoticeVersionNotFoundError();

        var record = Domain.ConsentRecord.Grant(
            request.SubjectId, ToDomain(request.Purpose), request.NoticeVersionId, request.GivenById, clock.UtcNow);

        await consentRecordRepository.AddAsync(record, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(record);
    }

    /// <inheritdoc />
    public async Task WithdrawConsentAsync(Guid consentRecordId, Guid withdrawnById, CancellationToken cancellationToken)
    {
        var record = await consentRecordRepository.GetByIdAsync(consentRecordId, cancellationToken);

        // Someone else's record is reported exactly as a missing one, so a caller learns nothing about which ids exist.
        if (record is null || record.SubjectId != withdrawnById)
        {
            throw new ConsentRecordNotFoundError();
        }

        record.Withdraw(withdrawnById, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: withdrawnById,
                ActorRole: null,
                Action: "Consent.Withdrawn",
                EntityType: "ConsentRecord",
                EntityId: record.Id.ToString(),
                Metadata: new Dictionary<string, string> { ["purpose"] = record.Purpose.ToString() },
                CorrelationId: null),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task EnsureConsentAsync(Guid subjectId, ConsentPurpose purpose, CancellationToken cancellationToken)
    {
        if (!await HasActiveConsentAsync(subjectId, purpose, cancellationToken))
        {
            throw new ConsentRequiredError();
        }
    }

    // Why only the subject: a guardian acting for a minor would need the caller to be proven as that guardian, and
    // nothing binds a guardian record to an account yet. Until it does, the strict rule is the only safe one.
    private static void EnsureOwnConsent(Guid subjectId, Guid requestedById)
    {
        if (subjectId != requestedById)
        {
            throw new ConsentAccessDeniedError();
        }
    }

    // Explicit mapping, not a cast, so the two enums silently drifting out of
    // matching order would fail loudly here instead of miscategorizing a purpose.
    private static Domain.ConsentPurpose ToDomain(ConsentPurpose purpose) => purpose switch
    {
        ConsentPurpose.TermsOfService => Domain.ConsentPurpose.TermsOfService,
        ConsentPurpose.PrivacyNotice => Domain.ConsentPurpose.PrivacyNotice,
        ConsentPurpose.ProctoringDataProcessing => Domain.ConsentPurpose.ProctoringDataProcessing,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unmapped consent purpose."),
    };

    private static ConsentPurpose ToContract(Domain.ConsentPurpose purpose) => purpose switch
    {
        Domain.ConsentPurpose.TermsOfService => ConsentPurpose.TermsOfService,
        Domain.ConsentPurpose.PrivacyNotice => ConsentPurpose.PrivacyNotice,
        Domain.ConsentPurpose.ProctoringDataProcessing => ConsentPurpose.ProctoringDataProcessing,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unmapped consent purpose."),
    };

    private static ConsentRecordDto ToDto(Domain.ConsentRecord record) => new(
        record.Id, record.SubjectId, ToContract(record.Purpose), record.NoticeVersionId, record.GrantedAtUtc, record.WithdrawnAtUtc);
}
