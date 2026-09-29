using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Application;
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
        var givenById = Guid.NewGuid();
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
}
