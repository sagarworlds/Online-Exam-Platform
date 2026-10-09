using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Where an attempt was sat from, and the change of address or device that multi-login detection looks for (FR-26).</summary>
public class ClientSightingTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly FakeClientInfo _client = new();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly QuestionSnapshot _question = Fixtures.Question();
    private readonly ExamSnapshot _exam;

    public ClientSightingTests()
    {
        _exam = Fixtures.Exam([_question]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_question]));
    }

    private Attempt OpenAttempt()
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);

    // ---- the attempt ----------------------------------------------------------------------------------

    [Fact]
    public void TheFirstSighting_IsWhereTheAttemptBegan()
    {
        var attempt = OpenAttempt();

        Assert.True(attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now));

        var seen = Assert.Single(attempt.ClientSightings);
        Assert.Equal("203.0.113.10", seen.IpAddress);
        Assert.Equal("device-a", seen.DeviceFingerprint);
        Assert.Equal(ClientSightingReason.Started, seen.Reason);
        Assert.Equal(0, attempt.ClientChanges);
        Assert.Equal(1, attempt.DeviceCount);
        Assert.Empty(attempt.DomainEvents.OfType<AttemptClientChangedEvent>());
    }

    [Fact]
    public void SeeingTheSamePlaceAgain_RecordsNothing()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now);

        Assert.False(attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now.AddMinutes(5)));

        Assert.Single(attempt.ClientSightings);
    }

    [Theory]
    [InlineData("203.0.113.99", "device-a")]
    [InlineData("203.0.113.10", "device-b")]
    [InlineData("203.0.113.99", "device-b")]
    [InlineData(null, "device-a")]
    [InlineData("203.0.113.10", null)]
    public void ADifferentAddressOrDevice_IsRecordedAsAChange_AndRaisesAnEventNamingBoth(string? ip, string? device)
    {
        var attempt = OpenAttempt();
        attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now);

        Assert.True(attempt.NoteClient(ip, device, Fixtures.Now.AddMinutes(5)));

        Assert.Equal(1, attempt.ClientChanges);
        var changed = Assert.Single(attempt.DomainEvents.OfType<AttemptClientChangedEvent>());
        Assert.Equal("203.0.113.10", changed.PreviousIpAddress);
        Assert.Equal("device-a", changed.PreviousDeviceFingerprint);
        Assert.Equal(ip, changed.IpAddress);
        Assert.Equal(device, changed.DeviceFingerprint);
    }

    [Fact]
    public void GoingBackToTheFirstDevice_IsAnotherChange_NotAReturnToNormal()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now);
        attempt.NoteClient("198.51.100.7", "device-b", Fixtures.Now.AddMinutes(5));

        Assert.True(attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now.AddMinutes(9)));

        Assert.Equal(2, attempt.ClientChanges);
        Assert.Equal(2, attempt.DeviceCount);
    }

    [Fact]
    public void DevicesAreCountedByDistinctSignature_AndAnUnknownDeviceIsNotOne()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient("203.0.113.10", null, Fixtures.Now);
        attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now.AddMinutes(1));
        attempt.NoteClient("198.51.100.7", "device-a", Fixtures.Now.AddMinutes(2));

        Assert.Equal(1, attempt.DeviceCount);
        Assert.Equal(2, attempt.ClientChanges);
    }

    [Fact]
    public void AFinishedAttempt_IsNotWatched_SoOpeningTheResultElsewhereIsNotAChange()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now);
        attempt.Submit(Fixtures.Now.AddMinutes(5), 0, 1);

        Assert.False(attempt.NoteClient("198.51.100.7", "device-b", Fixtures.Now.AddMinutes(9)));

        Assert.Single(attempt.ClientSightings);
    }

    // ---- the handlers -----------------------------------------------------------------------------------

    [Fact]
    public async Task LoadingTheAttemptFromAnotherDevice_IsRecordedAndSaved()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient(_client.IpAddress, _client.DeviceFingerprint, Fixtures.Now);
        var handler = new GetAttemptHandler(Access, new AttemptViewBuilder(_bank, _clock), _client, _unitOfWork, _clock);
        _client.DeviceFingerprint = "device-b";

        await handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(1, attempt.ClientChanges);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadingTheAttemptFromTheSamePlace_SavesNothing()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient(_client.IpAddress, _client.DeviceFingerprint, Fixtures.Now);
        var handler = new GetAttemptHandler(Access, new AttemptViewBuilder(_bank, _clock), _client, _unitOfWork, _clock);

        await handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheHeartbeatFromAnotherNetwork_IsRecordedAndSaved()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient(_client.IpAddress, _client.DeviceFingerprint, Fixtures.Now);
        _client.IpAddress = "198.51.100.7";

        await new GetAttemptStatusHandler(Access, _clock, _client, _unitOfWork).HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(1, attempt.ClientChanges);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StaffSeeTheSightingsOldestFirst_AndTheSummaryCountsThem()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now);
        attempt.NoteClient("198.51.100.7", "device-b", Fixtures.Now.AddMinutes(4));
        var staff = new StaffAttemptAccess(_attempts, _catalog, Access);

        var rows = await new ListAttemptClientsHandler(staff).HandleAsync(_exam.Id, attempt.Id, CancellationToken.None);

        Assert.Equal([ClientSightingReason.Started, ClientSightingReason.Changed], rows.Select(r => r.Reason));
        Assert.Equal(["device-a", "device-b"], rows.Select(r => r.DeviceFingerprint));
        var summary = ExamCandidateRows.StaffSummary(attempt);
        Assert.Equal(1, summary.ClientChanges);
        Assert.Equal(2, summary.Devices);
    }

    [Fact]
    public void TheCandidatesOwnSummary_DoesNotCarryWhereTheyWereSeen()
    {
        var attempt = OpenAttempt();
        attempt.NoteClient("203.0.113.10", "device-a", Fixtures.Now);
        attempt.NoteClient("198.51.100.7", "device-b", Fixtures.Now.AddMinutes(4));

        var summary = ExamCandidateRows.Summary(attempt);

        Assert.Null(summary.ClientChanges);
        Assert.Null(summary.Devices);
    }
}
