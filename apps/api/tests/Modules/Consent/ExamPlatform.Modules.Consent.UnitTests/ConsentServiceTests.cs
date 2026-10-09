using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Application;
using ExamPlatform.Modules.Consent.Application.Exceptions;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;
using DomainConsent = ExamPlatform.Modules.Consent.Domain;

namespace ExamPlatform.Modules.Consent.UnitTests;

public class ConsentServiceTests
{
    private static ConsentService CreateService(
        IConsentRecordRepository? recordRepository = null,
        INoticeVersionRepository? noticeVersionRepository = null,
        IAuditLogger? auditLogger = null,
        IConsentUnitOfWork? unitOfWork = null,
        Clock? clock = null) =>
        new(
            recordRepository ?? Substitute.For<IConsentRecordRepository>(),
            noticeVersionRepository ?? Substitute.For<INoticeVersionRepository>(),
            auditLogger ?? Substitute.For<IAuditLogger>(),
            unitOfWork ?? Substitute.For<IConsentUnitOfWork>(),
            clock ?? new FakeClock(DateTime.UtcNow));

    [Fact]
    public async Task EnsureConsentAsync_NoConsentRecord_ThrowsConsentRequiredError()
    {
        var recordRepository = Substitute.For<IConsentRecordRepository>();
        recordRepository
            .GetActiveAsync(Arg.Any<Guid>(), Arg.Any<DomainConsent.ConsentPurpose>(), Arg.Any<CancellationToken>())
            .Returns((DomainConsent.ConsentRecord?)null);

        var service = CreateService(recordRepository: recordRepository);

        await Assert.ThrowsAsync<ConsentRequiredError>(() =>
            service.EnsureConsentAsync(Guid.NewGuid(), ConsentPurpose.PrivacyNotice, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureConsentAsync_WithdrawnConsent_ThrowsConsentRequiredError()
    {
        // GetActiveAsync only ever returns non-withdrawn records (see ConsentRecordRepository),
        // so from the service's perspective a withdrawn consent looks identical to no consent at all.
        var recordRepository = Substitute.For<IConsentRecordRepository>();
        recordRepository
            .GetActiveAsync(Arg.Any<Guid>(), Arg.Any<DomainConsent.ConsentPurpose>(), Arg.Any<CancellationToken>())
            .Returns((DomainConsent.ConsentRecord?)null);

        var service = CreateService(recordRepository: recordRepository);

        await Assert.ThrowsAsync<ConsentRequiredError>(() =>
            service.EnsureConsentAsync(Guid.NewGuid(), ConsentPurpose.PrivacyNotice, CancellationToken.None));
    }

    [Fact]
    public async Task RecordConsentAsync_PersistsAndEmitsConsentGrantedEvent()
    {
        var noticeVersionId = Guid.NewGuid();
        var notice = DomainConsent.NoticeVersion.Create(
            DomainConsent.ConsentPurpose.PrivacyNotice, "v1", DateTime.UtcNow, "https://example.invalid/privacy-v1");

        var noticeVersionRepository = Substitute.For<INoticeVersionRepository>();
        noticeVersionRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(notice);

        var recordRepository = Substitute.For<IConsentRecordRepository>();
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var service = CreateService(recordRepository: recordRepository, noticeVersionRepository: noticeVersionRepository, unitOfWork: unitOfWork);

        var subjectId = Guid.NewGuid();
        // A person grants their own consent; a grant on someone else's behalf is refused (see the tests below).
        var givenById = subjectId;
        var result = await service.RecordConsentAsync(
            new RecordConsentRequest(subjectId, ConsentPurpose.PrivacyNotice, noticeVersionId, givenById),
            CancellationToken.None);

        Assert.Equal(subjectId, result.SubjectId);
        Assert.Null(result.WithdrawnAtUtc);

        await recordRepository.Received(1).AddAsync(
            Arg.Is<DomainConsent.ConsentRecord>(r =>
                r.SubjectId == subjectId && r.DomainEvents.OfType<DomainConsent.Events.ConsentGrantedEvent>().Any()),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordConsentAsync_GivenForAnotherPerson_ThrowsAndSavesNothing()
    {
        var recordRepository = Substitute.For<IConsentRecordRepository>();
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var service = CreateService(recordRepository: recordRepository, unitOfWork: unitOfWork);

        await Assert.ThrowsAsync<ConsentAccessDeniedError>(() =>
            service.RecordConsentAsync(
                new RecordConsentRequest(Guid.NewGuid(), ConsentPurpose.PrivacyNotice, Guid.NewGuid(), Guid.NewGuid()),
                CancellationToken.None));

        await recordRepository.DidNotReceive().AddAsync(Arg.Any<DomainConsent.ConsentRecord>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStatusAsync_RequestedByAnotherPerson_ThrowsWithoutReadingAnything()
    {
        var recordRepository = Substitute.For<IConsentRecordRepository>();
        var service = CreateService(recordRepository: recordRepository);

        await Assert.ThrowsAsync<ConsentAccessDeniedError>(() =>
            service.GetStatusAsync(Guid.NewGuid(), ConsentPurpose.PrivacyNotice, Guid.NewGuid(), CancellationToken.None));

        await recordRepository.DidNotReceive().GetActiveAsync(Arg.Any<Guid>(), Arg.Any<DomainConsent.ConsentPurpose>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStatusAsync_RequestedBySubject_ReportsTheActiveRecord()
    {
        var subjectId = Guid.NewGuid();
        var record = DomainConsent.ConsentRecord.Grant(
            subjectId, DomainConsent.ConsentPurpose.PrivacyNotice, Guid.NewGuid(), subjectId, DateTime.UtcNow);

        var recordRepository = Substitute.For<IConsentRecordRepository>();
        recordRepository
            .GetActiveAsync(subjectId, DomainConsent.ConsentPurpose.PrivacyNotice, Arg.Any<CancellationToken>())
            .Returns(record);
        var noticeVersionRepository = Substitute.For<INoticeVersionRepository>();
        noticeVersionRepository
            .GetCurrentAsync(DomainConsent.ConsentPurpose.PrivacyNotice, Arg.Any<CancellationToken>())
            .Returns((DomainConsent.NoticeVersion?)null);
        var service = CreateService(recordRepository: recordRepository, noticeVersionRepository: noticeVersionRepository);

        var status = await service.GetStatusAsync(subjectId, ConsentPurpose.PrivacyNotice, subjectId, CancellationToken.None);

        Assert.True(status.IsActive);
        Assert.Equal(record.Id, status.ConsentRecordId);
    }

    [Fact]
    public async Task WithdrawConsentAsync_ByAnotherPerson_ReportsNotFoundAndKeepsTheConsent()
    {
        var subjectId = Guid.NewGuid();
        var record = DomainConsent.ConsentRecord.Grant(
            subjectId, DomainConsent.ConsentPurpose.PrivacyNotice, Guid.NewGuid(), subjectId, DateTime.UtcNow);

        var recordRepository = Substitute.For<IConsentRecordRepository>();
        recordRepository.GetByIdAsync(record.Id, Arg.Any<CancellationToken>()).Returns(record);
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var service = CreateService(recordRepository: recordRepository, unitOfWork: unitOfWork);

        await Assert.ThrowsAsync<ConsentRecordNotFoundError>(() =>
            service.WithdrawConsentAsync(record.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Null(record.WithdrawnAtUtc);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithdrawConsentAsync_ByTheSubject_WithdrawsTheRecord()
    {
        var subjectId = Guid.NewGuid();
        var record = DomainConsent.ConsentRecord.Grant(
            subjectId, DomainConsent.ConsentPurpose.PrivacyNotice, Guid.NewGuid(), subjectId, DateTime.UtcNow);

        var recordRepository = Substitute.For<IConsentRecordRepository>();
        recordRepository.GetByIdAsync(record.Id, Arg.Any<CancellationToken>()).Returns(record);
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var service = CreateService(recordRepository: recordRepository, unitOfWork: unitOfWork);

        await service.WithdrawConsentAsync(record.Id, subjectId, CancellationToken.None);

        Assert.NotNull(record.WithdrawnAtUtc);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
