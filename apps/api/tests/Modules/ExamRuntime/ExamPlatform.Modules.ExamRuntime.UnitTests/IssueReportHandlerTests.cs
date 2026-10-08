using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Reporting a problem from inside an exam, and staff listing and resolving what was reported (FR-42).</summary>
public class IssueReportHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IIssueReportRepository _reports = Substitute.For<IIssueReportRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly QuestionSnapshot _question = Fixtures.Question("2 + 2?");

    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);

    private ReportIssueHandler Report => new(Access, _reports, _unitOfWork, _clock);

    private IssueReportDtoFactory Dtos => new(_catalog, _roster, _attempts, _bank);

    /// <summary>A published exam holding the question, and the candidate's attempt at it, open with half an hour left.</summary>
    private (ExamSnapshot Exam, Attempt Attempt) Open(Guid? owner = null)
    {
        var exam = Fixtures.Exam([_question]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        var attempt = Attempt.Start(exam.Id, owner ?? _candidate, 1, Fixtures.Now.AddMinutes(-5), Fixtures.Now.AddMinutes(25));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return (exam, attempt);
    }

    private IssueReport Reported(Guid attemptId, Guid examId, Guid? question = null) =>
        IssueReport.Raise(attemptId, examId, _candidate, question, IssueCategory.Question, "Option C is missing", Fixtures.Now.AddMinutes(-2));

    // ---- reporting -------------------------------------------------------------------------------------

    [Fact]
    public async Task ACandidate_CanReportAProblemFromTheirOpenAttempt_AndItIsSavedOpen()
    {
        var (exam, attempt) = Open();
        IssueReport? added = null;
        _reports.When(r => r.Add(Arg.Any<IssueReport>())).Do(call => added = call.Arg<IssueReport>());

        var result = await Report.HandleAsync(
            attempt.Id, _candidate, IssueCategory.Question, _question.Id, "  Option C is missing  ", CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(IssueReportStatus.Open, added.Status);
        Assert.Equal(attempt.Id, added.AttemptId);
        Assert.Equal(exam.Id, added.ExamId);
        Assert.Equal(_candidate, added.CandidateId);
        Assert.Equal(_question.Id, added.QuestionId);
        Assert.Equal(Fixtures.Now, added.ReportedAtUtc);
        Assert.Equal(added.Id, result.Id);
        Assert.Equal("Option C is missing", result.Message);
        Assert.Equal(IssueCategory.Question, result.Category);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReportAboutThePageRatherThanAQuestion_NeedsNoQuestion()
    {
        var (_, attempt) = Open();

        var result = await Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Technical, null, "The timer froze", CancellationToken.None);

        Assert.Null(result.QuestionId);
        Assert.Equal(IssueCategory.Technical, result.Category);
    }

    [Fact]
    public async Task AReport_DoesNotChangeTheAttempt()
    {
        var (_, attempt) = Open();

        await Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Other, null, "Something odd", CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.Null(attempt.PausedAtUtc);
    }

    [Fact]
    public async Task SomeoneElsesAttempt_AnswersLikeAMissingOne_AndSavesNothing()
    {
        var (_, attempt) = Open(owner: Guid.NewGuid());

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Other, null, "Hello", CancellationToken.None));

        _reports.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ASubmittedAttempt_IsNoLongerInTheExam_SoItCannotReport()
    {
        var (_, attempt) = Open();
        attempt.Submit(Fixtures.Now.AddMinutes(-1), 0, 1);

        var error = await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Other, null, "Hello", CancellationToken.None));

        Assert.Equal("attempt_not_in_progress", error.ErrorCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task APausedAttempt_MayReport()
    {
        var (_, attempt) = Open();
        attempt.Pause(Fixtures.Now.AddMinutes(-1));

        var result = await Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Technical, null, "Why was I stopped?", CancellationToken.None);

        Assert.Equal(IssueCategory.Technical, result.Category);
    }

    [Fact]
    public async Task AQuestionThatWasNotInTheAttempt_CannotBeNamed()
    {
        var (_, attempt) = Open();

        await Assert.ThrowsAsync<QuestionNotInAttemptError>(
            () => Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Question, Guid.NewGuid(), "Hello", CancellationToken.None));
    }

    [Fact]
    public async Task AnAttempt_CarriesOnlySoManyReports()
    {
        var (_, attempt) = Open();
        _reports.CountForAttemptAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(IssueReport.MaxPerAttempt);

        var error = await Assert.ThrowsAsync<TooManyIssueReportsError>(
            () => Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Other, null, "Hello", CancellationToken.None));

        Assert.Equal("too_many_issue_reports", error.ErrorCode);
        Assert.Equal(429, error.HttpStatusCode);
        _reports.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task OneBelowTheLimit_StillReports()
    {
        var (_, attempt) = Open();
        _reports.CountForAttemptAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(IssueReport.MaxPerAttempt - 1);

        var result = await Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Other, null, "The last one", CancellationToken.None);

        Assert.Equal("The last one", result.Message);
    }

    [Fact]
    public async Task AReportWithoutADescription_IsRefused_AndSavesNothing()
    {
        var (_, attempt) = Open();

        await Assert.ThrowsAsync<InvalidAttemptError>(
            () => Report.HandleAsync(attempt.Id, _candidate, IssueCategory.Other, null, "   ", CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // ---- listing and resolving -------------------------------------------------------------------------

    private void KnowTheExam(ExamSnapshot exam, Attempt attempt)
    {
        _roster.GetEnrolledCandidatesAsync(exam.Id, Arg.Any<CancellationToken>()).Returns([new EnrolledCandidate(_candidate, "asha@example.com")]);
        _attempts.ListForExamAsync(exam.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_question]);
    }

    [Fact]
    public async Task TheQueue_ListsOpenReportsByDefault_WithWhoWhichExamWhichAttemptAndWhichQuestion()
    {
        var (exam, attempt) = Open();
        KnowTheExam(exam, attempt);
        var report = Reported(attempt.Id, exam.Id, _question.Id);
        _reports.ListAsync(IssueReportStatus.Open, ListIssueReportsHandler.PageSize, Arg.Any<CancellationToken>()).Returns([report]);

        var listed = Assert.Single(await new ListIssueReportsHandler(_reports, Dtos).HandleAsync(null, CancellationToken.None));

        Assert.Equal(report.Id, listed.Id);
        Assert.Equal(exam.Name, listed.ExamName);
        Assert.Equal(attempt.Id, listed.AttemptId);
        Assert.Equal(1, listed.AttemptNumber);
        Assert.Equal("asha@example.com", listed.CandidateEmail);
        Assert.Equal(_question.Id, listed.QuestionId);
        Assert.Equal("2 + 2?", listed.QuestionText);
        Assert.Equal("Option C is missing", listed.Message);
        Assert.Equal(IssueReportStatus.Open, listed.Status);
    }

    [Fact]
    public async Task AReportAboutNoQuestion_ListsWithoutOne_AndNeverAsksTheQuestionBank()
    {
        var (exam, attempt) = Open();
        KnowTheExam(exam, attempt);
        _reports.ListAsync(IssueReportStatus.Open, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([Reported(attempt.Id, exam.Id)]);

        var listed = Assert.Single(await new ListIssueReportsHandler(_reports, Dtos).HandleAsync(null, CancellationToken.None));

        Assert.Null(listed.QuestionId);
        Assert.Null(listed.QuestionText);
        await _bank.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }

    [Fact]
    public async Task TheQueue_CanListResolvedReports()
    {
        _reports.ListAsync(IssueReportStatus.Resolved, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await new ListIssueReportsHandler(_reports, Dtos).HandleAsync(IssueReportStatus.Resolved, CancellationToken.None));

        await _reports.Received(1).ListAsync(IssueReportStatus.Resolved, ListIssueReportsHandler.PageSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReportWhoseExamIsGone_StillLists_WithTheseLeftOut()
    {
        var gone = Reported(Guid.NewGuid(), Guid.NewGuid());
        _catalog.FindAsync(gone.ExamId, Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);
        _roster.GetEnrolledCandidatesAsync(gone.ExamId, Arg.Any<CancellationToken>()).Returns([]);
        _attempts.ListForExamAsync(gone.ExamId, Arg.Any<CancellationToken>()).Returns([]);
        _reports.ListAsync(IssueReportStatus.Open, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([gone]);

        var listed = Assert.Single(await new ListIssueReportsHandler(_reports, Dtos).HandleAsync(null, CancellationToken.None));

        Assert.Null(listed.ExamName);
        Assert.Null(listed.AttemptNumber);
        Assert.Null(listed.CandidateEmail);
        Assert.Equal("Option C is missing", listed.Message);
    }

    [Fact]
    public async Task Resolving_SettlesTheReport_SavesIt_AndReturnsTheNote()
    {
        var (exam, attempt) = Open();
        KnowTheExam(exam, attempt);
        var report = Reported(attempt.Id, exam.Id);
        _reports.GetByIdAsync(report.Id, Arg.Any<CancellationToken>()).Returns(report);

        var result = await new ResolveIssueReportHandler(_reports, _unitOfWork, Dtos, _clock)
            .HandleAsync(report.Id, _staff, "  Fixed the option  ", CancellationToken.None);

        Assert.Equal(IssueReportStatus.Resolved, result.Status);
        Assert.Equal("Fixed the option", result.ResolutionNote);
        Assert.Equal(Fixtures.Now, result.ResolvedAtUtc);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvingAnUnknownReport_IsNotFound()
    {
        _reports.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IssueReport?)null);

        var error = await Assert.ThrowsAsync<IssueReportNotFoundError>(
            () => new ResolveIssueReportHandler(_reports, _unitOfWork, Dtos, _clock).HandleAsync(Guid.NewGuid(), _staff, null, CancellationToken.None));

        Assert.Equal("issue_report_not_found", error.ErrorCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ResolvingAResolvedReport_IsRefused_AndSavesNothing()
    {
        var (exam, attempt) = Open();
        var report = Reported(attempt.Id, exam.Id);
        report.Resolve(_staff, Fixtures.Now, "Done");
        _reports.GetByIdAsync(report.Id, Arg.Any<CancellationToken>()).Returns(report);

        await Assert.ThrowsAsync<IssueReportNotOpenError>(
            () => new ResolveIssueReportHandler(_reports, _unitOfWork, Dtos, _clock).HandleAsync(report.Id, _staff, "Again", CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
