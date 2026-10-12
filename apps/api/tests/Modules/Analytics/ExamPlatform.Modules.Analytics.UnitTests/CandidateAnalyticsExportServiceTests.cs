using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Analytics.Application;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>A candidate's analytics export is recorded before it is handed out, names the candidate as the one who took it, and holds only their figures (FR-38, FR-40).</summary>
public class CandidateAnalyticsExportServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc);

    private readonly Guid _candidate = Guid.NewGuid();
    private readonly ICandidateAnalytics _analytics = Substitute.For<ICandidateAnalytics>();
    private readonly ICandidateAnalyticsCsvWriter _csv = Substitute.For<ICandidateAnalyticsCsvWriter>();
    private readonly ICandidateAnalyticsPdfWriter _pdf = Substitute.For<ICandidateAnalyticsPdfWriter>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly FakeClock _clock = new(Now);

    private CandidateAnalyticsExportService Service => new(_analytics, _csv, _pdf, _audit, _clock);

    private CandidateAnalyticsExportRequest Request(ReportFormat format = ReportFormat.Csv, string? correlationId = "req-9") =>
        new(_candidate, "Candidate", correlationId, format);

    private CandidateAnalyticsDto Figures(int results = 2, int sections = 3)
    {
        var trend = Enumerable.Range(1, results)
            .Select(i => new ScorePointDto(Guid.NewGuid(), Guid.NewGuid(), $"Mock {i}", Now.AddDays(-i), 10, 20, 50m))
            .ToList();
        var sectionList = Enumerable.Range(1, sections)
            .Select(i => new SectionPerformanceDto($"S{i}", 1, 1, 0, 0, 0, 100m))
            .ToList();
        var figures = new CandidateAnalyticsDto(results, trend, sectionList, [], new TimePerQuestionDto(0, null, []));
        _analytics.GetAsync(_candidate, Arg.Any<CancellationToken>()).Returns(figures);
        _csv.Write(figures).Returns(new byte[] { 1 });
        _pdf.Write(figures).Returns(new byte[] { 2 });
        return figures;
    }

    [Fact]
    public async Task AnExport_IsRecorded_WithTheCandidateAsTheActor_AndTheirOwnRecordAsTheEntity()
    {
        Figures();

        await Service.ExportAsync(Request(), CancellationToken.None);

        await _audit.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e =>
                e.ActorUserId == _candidate
                && e.ActorRole == "Candidate"
                && e.Action == CandidateAnalyticsExportService.ExportAction
                && e.EntityType == "Candidate"
                && e.EntityId == _candidate.ToString()
                && e.CorrelationId == "req-9"
                && e.Metadata["report"] == "candidate-analytics"
                && e.Metadata["format"] == "csv"
                && e.Metadata["results"] == "2"
                && e.Metadata["sections"] == "3"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheFigures_AreTheCandidatesOwn_ForTheCandidateWhoAsked()
    {
        Figures();

        await Service.ExportAsync(Request(), CancellationToken.None);

        await _analytics.Received(1).GetAsync(_candidate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACsvExport_IsNamedForTheTime_AndIsATextFile()
    {
        Figures();

        var report = await Service.ExportAsync(Request(ReportFormat.Csv), CancellationToken.None);

        Assert.Equal("candidate-analytics-20261010-0930.csv", report.FileName);
        Assert.Equal("text/csv", report.ContentType);
        Assert.Equal(new byte[] { 1 }, report.Content);
        Assert.Equal(2, report.RowCount);
    }

    [Fact]
    public async Task APdfExport_IsRecordedAsPdf_AndWrittenByThePdfWriter()
    {
        Figures();

        var report = await Service.ExportAsync(Request(ReportFormat.Pdf), CancellationToken.None);

        await _audit.Received(1).RecordAsync(Arg.Is<AuditEntry>(e => e.Metadata["format"] == "pdf"), Arg.Any<CancellationToken>());
        _pdf.Received(1).Write(Arg.Any<CandidateAnalyticsDto>());
        Assert.Equal("candidate-analytics-20261010-0930.pdf", report.FileName);
        Assert.Equal("application/pdf", report.ContentType);
        Assert.Equal(new byte[] { 2 }, report.Content);
    }

    [Fact]
    public async Task ACandidateWithNoResults_StillGetsAFile_AndAnAuditEntry()
    {
        Figures(results: 0, sections: 0);

        var report = await Service.ExportAsync(Request(), CancellationToken.None);

        Assert.Equal(0, report.RowCount);
        await _audit.Received(1).RecordAsync(Arg.Is<AuditEntry>(e => e.Metadata["results"] == "0"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExportThatCannotBeRecorded_IsNotHandedOut()
    {
        Figures();
        _audit.RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("database unavailable")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ExportAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task AFormatThisModuleDoesNotWrite_IsRefused_AndNothingIsRecorded()
    {
        Figures();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service.ExportAsync(Request((ReportFormat)99), CancellationToken.None));

        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }
}
