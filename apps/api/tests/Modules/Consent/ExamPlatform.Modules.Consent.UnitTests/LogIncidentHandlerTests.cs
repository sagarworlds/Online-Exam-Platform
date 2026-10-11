using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Consent.Application;
using ExamPlatform.Modules.Consent.Application.Commands;
using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.Modules.Consent.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.Consent.UnitTests;

public class LogIncidentHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static LogIncidentHandler CreateHandler(
        IIncidentRepository? repository = null,
        IConsentUnitOfWork? unitOfWork = null,
        IAuditLogger? auditLogger = null) =>
        new(
            repository ?? Substitute.For<IIncidentRepository>(),
            unitOfWork ?? Substitute.For<IConsentUnitOfWork>(),
            auditLogger ?? Substitute.For<IAuditLogger>(),
            new FakeClock(Now));

    [Fact]
    public async Task HandleAsync_SavesTheIncident_AuditsOnlyItsIdAndCategory_AndReturnsIt()
    {
        var repository = Substitute.For<IIncidentRepository>();
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var auditLogger = Substitute.For<IAuditLogger>();
        var handler = CreateHandler(repository, unitOfWork, auditLogger);
        var loggedBy = Guid.NewGuid();
        const string description = "Roll number 4471 was printed on a shared screen";

        var result = await handler.HandleAsync(
            new LogIncidentCommand(description, Now.AddHours(-1), IncidentCategory.DataBreach, "Names and scores", loggedBy),
            CancellationToken.None);

        await repository.Received(1).AddAsync(Arg.Is<Incident>(i => i.Id == result.Id), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var audit = Assert.Single(auditLogger.ReceivedCalls()).GetArguments()[0] as AuditEntry;
        Assert.NotNull(audit);
        Assert.Equal("Incident.Logged", audit.Action);
        Assert.Equal(loggedBy, audit.ActorUserId);
        Assert.Equal(result.Id.ToString(), audit.EntityId);
        Assert.Equal(new Dictionary<string, string> { ["category"] = "DataBreach" }, audit.Metadata);

        Assert.Equal(description, result.Description);
        Assert.Equal(IncidentStatus.Logged, result.Status);
        Assert.Equal(Now.AddHours(5), result.EscalationDueAtUtc);
        Assert.False(result.IsOverdue);
    }

    [Fact]
    public async Task HandleAsync_DetectionTimeInTheFuture_ThrowsAndSavesNothing()
    {
        var repository = Substitute.For<IIncidentRepository>();
        var unitOfWork = Substitute.For<IConsentUnitOfWork>();
        var auditLogger = Substitute.For<IAuditLogger>();
        var handler = CreateHandler(repository, unitOfWork, auditLogger);

        await Assert.ThrowsAsync<InvalidIncidentError>(() => handler.HandleAsync(
            new LogIncidentCommand("A breach", Now.AddMinutes(1), IncidentCategory.DataBreach, "Names", Guid.NewGuid()),
            CancellationToken.None));

        await repository.DidNotReceive().AddAsync(Arg.Any<Incident>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await auditLogger.DidNotReceive().RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DetectedMoreThanSixHoursAgo_IsOverdueOnArrival()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync(
            new LogIncidentCommand("A breach", Now.AddHours(-7), IncidentCategory.DataBreach, "Names", Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsOverdue);
        Assert.Equal(Now.AddHours(-1), result.EscalationDueAtUtc);
    }
}
