using ExamPlatform.Modules.Consent.Application.Ports;
using ExamPlatform.Modules.Consent.Application.Queries;
using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.Consent.UnitTests;

public class ListOpenIncidentsHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static Incident LoggedDetectedAt(DateTime detectedAtUtc) =>
        Incident.Log("A breach", detectedAtUtc, IncidentCategory.DataBreach, "Names", Guid.NewGuid(), Now);

    [Fact]
    public async Task HandleAsync_FlagsEachIncidentAsOverdueAtTheMomentOfReading()
    {
        var overdue = LoggedDetectedAt(Now.AddHours(-7));
        var withinWindow = LoggedDetectedAt(Now.AddHours(-1));
        var repository = Substitute.For<IIncidentRepository>();
        repository.ListOpenAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<Incident> { overdue, withinWindow });
        var handler = new ListOpenIncidentsHandler(repository, new FakeClock(Now));

        var result = await handler.HandleAsync(PageRequest.Create(null, null), CancellationToken.None);

        Assert.Collection(
            result,
            first => Assert.True(first.IsOverdue),
            second => Assert.False(second.IsOverdue));
    }

    [Fact]
    public async Task HandleAsync_ReportedIncidentPastItsDueTime_IsNotFlagged()
    {
        var reported = LoggedDetectedAt(Now.AddHours(-10));
        reported.ChangeStatus(IncidentStatus.Reported, "Reported to CERT-In", Guid.NewGuid(), Now);
        var repository = Substitute.For<IIncidentRepository>();
        repository.ListOpenAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<Incident> { reported });
        var handler = new ListOpenIncidentsHandler(repository, new FakeClock(Now));

        var result = await handler.HandleAsync(PageRequest.Create(null, null), CancellationToken.None);

        Assert.False(Assert.Single(result).IsOverdue);
    }

    [Fact]
    public async Task HandleAsync_PassesThePageThroughToTheRepository()
    {
        var repository = Substitute.For<IIncidentRepository>();
        repository.ListOpenAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<Incident>());
        var handler = new ListOpenIncidentsHandler(repository, new FakeClock(Now));
        var page = PageRequest.Create(2, 10);

        await handler.HandleAsync(page, CancellationToken.None);

        await repository.Received(1).ListOpenAsync(page, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoOpenIncidents_ReturnsAnEmptyList()
    {
        var repository = Substitute.For<IIncidentRepository>();
        repository.ListOpenAsync(Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<Incident>());
        var handler = new ListOpenIncidentsHandler(repository, new FakeClock(Now));

        var result = await handler.HandleAsync(PageRequest.Create(null, null), CancellationToken.None);

        Assert.Empty(result);
    }
}
