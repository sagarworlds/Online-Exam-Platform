using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>An item analysis export is recorded before it is handed out, and names who took it and of which exam (FR-38, FR-40).</summary>
public class ItemAnalysisExportServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc);

    private readonly Guid _examId = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly IExamItemAnalysis _analysis = Substitute.For<IExamItemAnalysis>();
    private readonly IItemAnalysisCsvWriter _writer = Substitute.For<IItemAnalysisCsvWriter>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly FakeClock _clock = new(Now);

    private ItemAnalysisExportService Service => new(_analysis, _writer, _audit, _clock);

    private ItemAnalysisExportRequest Request(string? correlationId = "req-1") =>
        new(_examId, _actor, "ExamAdmin", correlationId);

    private void AnalysisOf(string examName = "Physics Final", int questions = 2)
    {
        var rows = Enumerable.Range(1, questions)
            .Select(i => new ItemRowDto(Guid.NewGuid(), i, $"Q{i}", 44, 30, 0.5m, 0m))
            .ToArray();
        _analysis.GetAsync(_examId, Arg.Any<CancellationToken>()).Returns(
            new ExamItemAnalysisDto(_examId, examName, ResultsReleased: true, CandidateCount: 44, MinimumCohortSize: 30, GroupSize: 12, rows));
        _writer.Write(Arg.Any<ExamItemAnalysisDto>()).Returns(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task AnExport_IsRecorded_WithWhoWhatAndWhichExam()
    {
        AnalysisOf();

        await Service.ExportAsync(Request(), CancellationToken.None);

        await _audit.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e =>
                e.ActorUserId == _actor
                && e.ActorRole == "ExamAdmin"
                && e.Action == ItemAnalysisExportService.ExportAction
                && e.EntityType == "Exam"
                && e.EntityId == _examId.ToString()
                && e.CorrelationId == "req-1"
                && e.Metadata["report"] == "item-analysis"
                && e.Metadata["format"] == "csv"
                && e.Metadata["rows"] == "2"
                && e.Metadata["candidates"] == "44"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheFile_IsNamedForTheExamAndTheTime_AndCarriesTheWritersBytes()
    {
        AnalysisOf("Physics Final");

        var report = await Service.ExportAsync(Request(), CancellationToken.None);

        Assert.Equal("item-analysis-physics-final-20261010-0930.csv", report.FileName);
        Assert.Equal("text/csv", report.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, report.Content);
        Assert.Equal(2, report.RowCount);
    }

    [Fact]
    public async Task AnUnknownExam_IsRefused_AndNothingIsRecorded()
    {
        _analysis.GetAsync(_examId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ExamItemAnalysisDto>(new ExamNotFoundError()));

        await Assert.ThrowsAsync<ExamNotFoundError>(() => Service.ExportAsync(Request(), CancellationToken.None));

        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task AnExportThatCannotBeRecorded_IsNotHandedOut()
    {
        // The audit trail is what makes the export accountable, so a failed record fails the export; the caller gets no file.
        AnalysisOf();
        _audit.RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("database unavailable")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ExportAsync(Request(), CancellationToken.None));
    }
}

/// <summary>A clock held at one instant, so a file name is the same on every run.</summary>
internal sealed class FakeClock(DateTime now) : ExamPlatform.SharedKernel.Application.Clock
{
    public DateTime UtcNow { get; set; } = now;
}
