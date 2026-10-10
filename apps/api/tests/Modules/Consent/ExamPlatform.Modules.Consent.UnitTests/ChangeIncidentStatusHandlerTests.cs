using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Application;
using ExamPlatform.Modules.Consent.Application.Commands;
using ExamPlatform.Modules.Consent.Application.Exceptions;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.Modules.Consent.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.Consent.UnitTests;

public class ChangeIncidentStatusHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static Incident LoggedIncident() =>
        Incident.Log("A staff laptop was lost", Now.AddHours(-1), IncidentCategory.DataBreach, "Names", Guid.NewGuid(), Now);

    private static ChangeIncidentStatusHandler CreateHandler(
        IIncidentRepository repository,
        IConsentUnitOfWork? unitOfWork = null,
        IAuditLogger? auditLogger = null) =>
        new(repository, unitOfWork ?? Substitute.For<IConsentUnitOfWork>(), auditLogger ?? Substitute.For<IAuditLogger>(), new FakeClock(Now));

    [Fact]
    public async Task HandleAsync_UnknownIncident_ThrowsNotFoundAndSavesNothing()
    {
        var repository = Substitute.For<IIncidentRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Incident?)null);
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var handler = CreateHandler(repository, unitOfWork);

        var error = await Assert.ThrowsAsync<IncidentNotFoundError>(() => handler.HandleAsync(
            new ChangeIncidentStatusCommand(Guid.NewGuid(), IncidentStatus.Reported, "A note", Guid.NewGuid()),
            CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ValidChange_SavesAuditsBothStatuses_AndReturnsTheIncident()
    {
        var incident = LoggedIncident();
        var repository = Substitute.For<IIncidentRepository>();
        repository.GetByIdAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var auditLogger = Substitute.For<IAuditLogger>();
        var handler = CreateHandler(repository, unitOfWork, auditLogger);
        var changedBy = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new ChangeIncidentStatusCommand(incident.Id, IncidentStatus.Reported, "Reported to CERT-In", changedBy),
            CancellationToken.None);

        Assert.Equal(IncidentStatus.Reported, result.Status);
        var change = Assert.Single(result.StatusChanges);
        Assert.Equal(changedBy, change.ChangedById);
        Assert.Equal("Reported to CERT-In", change.Note);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var audit = Assert.Single(auditLogger.ReceivedCalls()).GetArguments()[0] as AuditEntry;
        Assert.NotNull(audit);
        Assert.Equal("Incident.StatusChanged", audit.Action);
        Assert.Equal(changedBy, audit.ActorUserId);
        Assert.Equal(
            new Dictionary<string, string> { ["from"] = "Logged", ["to"] = "Reported" },
            audit.Metadata);
    }

    [Fact]
    public async Task HandleAsync_RefusedMove_ThrowsAndSavesNothing()
    {
        var incident = LoggedIncident();
        incident.ChangeStatus(IncidentStatus.Closed, "Closed as a false alarm", Guid.NewGuid(), Now);
        var repository = Substitute.For<IIncidentRepository>();
        repository.GetByIdAsync(incident.Id, Arg.Any<CancellationToken>()).Returns(incident);
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var auditLogger = Substitute.For<IAuditLogger>();
        var handler = CreateHandler(repository, unitOfWork, auditLogger);

        await Assert.ThrowsAsync<IncidentStatusChangeNotAllowedError>(() => handler.HandleAsync(
            new ChangeIncidentStatusCommand(incident.Id, IncidentStatus.Reported, "Too late", Guid.NewGuid()),
            CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditLogger.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
