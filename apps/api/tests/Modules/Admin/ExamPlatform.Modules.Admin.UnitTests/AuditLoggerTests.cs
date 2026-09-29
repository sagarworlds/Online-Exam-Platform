using ExamPlatform.Modules.Admin.Application;
using ExamPlatform.Modules.Admin.Application.Ports;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Admin.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Admin.UnitTests;

public class AuditLoggerTests
{
    [Fact]
    public async Task RecordAsync_PersistsEntryAndCommitsUnitOfWork()
    {
        var repository = Substitute.For<IAuditLogRepository>();
        var unitOfWork = Substitute.For<IAdminUnitOfWork>();
        var logger = new AuditLogger(repository, unitOfWork, new FakeClock(DateTime.UtcNow));

        var entry = new AuditEntry(
            ActorUserId: Guid.NewGuid(),
            ActorRole: "SuperAdmin",
            Action: "Consent.Withdrawn",
            EntityType: "ConsentRecord",
            EntityId: Guid.NewGuid().ToString(),
            Metadata: new Dictionary<string, string> { ["purpose"] = "PrivacyNotice" },
            CorrelationId: null);

        await logger.RecordAsync(entry, CancellationToken.None);

        await repository.Received(1).AddAsync(
            Arg.Is<AuditLog>(a => a.Action == "Consent.Withdrawn" && a.EntityType == "ConsentRecord"),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_WhenUnitOfWorkThrows_PropagatesRatherThanSwallows()
    {
        // The specific DbUpdateException catch/log/rethrow lives in AdminUnitOfWork
        // (Infrastructure) since it needs EF Core's exception type; this test only
        // proves AuditLogger itself never swallows whatever the unit of work throws.
        var repository = Substitute.For<IAuditLogRepository>();
        var unitOfWork = Substitute.For<IAdminUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new InvalidOperationException("db down"));
        var logger = new AuditLogger(repository, unitOfWork, new FakeClock(DateTime.UtcNow));

        var entry = new AuditEntry(null, null, "Consent.Withdrawn", "ConsentRecord", "id", new Dictionary<string, string>(), null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => logger.RecordAsync(entry, CancellationToken.None));
    }
}
