using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Exercises the candidate's path through an exam with the other modules replaced by fakes.</summary>
public class AttemptHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IEnrollments _enrollments = Substitute.For<IEnrollments>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

    private readonly QuestionSnapshot _q1 = Fixtures.Question("First");
    private readonly QuestionSnapshot _q2 = Fixtures.Question("Second");
    private readonly ExamSnapshot _exam;

    public AttemptHandlerTests()
    {
        _exam = Fixtures.Exam([_q1, _q2]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _enrollments.IsEnrolledAsync(_candidate, _exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _q1, _q2 }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private AttemptViewBuilder Views => new(_bank, _clock);
    private AttemptCloser Closer => new(_bank, _unitOfWork, _clock);
    private AttemptAccess Access => new(_attempts, _catalog, Closer, _clock);
    private StartAttemptHandler Start => new(_catalog, _enrollments, _attempts, _unitOfWork, Access, Views, _clock);
    private SaveAnswerHandler Save => new(Access, _bank, _unitOfWork, _clock);
    private SubmitAttemptHandler Submit => new(Access, Closer, Views);
    private GetAttemptHandler Get => new(Access, Views);

    private Attempt OpenAttempt(DateTime? startedAt = null, DateTime? deadline = null)
    {
        var started = startedAt ?? Fixtures.Now;
        var attempt = Attempt.Start(_exam.Id, _candidate, started, deadline ?? started.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        _attempts.FindAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    // ---- start -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Start_CreatesAnAttemptWithTheServerDeadline_AndShowsTheQuestionsWithoutTheAnswerKey()
    {
        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.InProgress, dto.Status);
        Assert.Equal(Fixtures.Now.AddSeconds(1800), dto.DeadlineUtc);
        Assert.Equal(Fixtures.Now, dto.ServerTimeUtc);
        var section = Assert.Single(dto.Sections);
        Assert.Equal(["First", "Second"], section.Questions.Select(q => q.Text));
        Assert.All(section.Questions, q => Assert.Equal(3, q.Options.Count));
        _attempts.Received(1).Add(Arg.Is<Attempt>(a => a.CandidateId == _candidate && a.ExamId == _exam.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_ThatWouldRunPastTheEndOfTheWindow_IsCutOffAtTheEnd()
    {
        var exam = Fixtures.Exam([_q1], end: Fixtures.Now.AddMinutes(10), durationSeconds: 3600);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _enrollments.IsEnrolledAsync(_candidate, exam.Id, Arg.Any<CancellationToken>()).Returns(true);

        var dto = await Start.HandleAsync(exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(Fixtures.Now.AddMinutes(10), dto.DeadlineUtc);
    }

    [Fact]
    public async Task Start_WhenAnAttemptExists_ReturnsItWithItsOriginalDeadline_AndCreatesNoSecondOne()
    {
        var existing = OpenAttempt(startedAt: Fixtures.Now.AddMinutes(-5), deadline: Fixtures.Now.AddMinutes(25));
        existing.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now.AddMinutes(-4));

        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(existing.Id, dto.Id);
        Assert.Equal(Fixtures.Now.AddMinutes(25), dto.DeadlineUtc);
        Assert.Equal(_q1.Correct(), dto.Sections.Single().Questions.First().SelectedOptionId);
        _attempts.DidNotReceive().Add(Arg.Any<Attempt>());
    }

    [Fact]
    public async Task Start_WhenTheExistingAttemptRanOutOfTime_ClosesItAndReturnsTheResultInsteadOfQuestions()
    {
        var existing = OpenAttempt(startedAt: Fixtures.Now.AddMinutes(-40), deadline: Fixtures.Now.AddMinutes(-10));
        existing.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now.AddMinutes(-39));

        var dto = await Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, dto.Status);
        Assert.True(dto.AutoSubmitted);
        Assert.Equal(1m, dto.Score);
        Assert.Equal(2m, dto.MaxScore);
        Assert.Empty(dto.Sections);
    }

    [Fact]
    public async Task Start_WhenNotEnrolled_ReportsTheExamAsNotAvailable()
    {
        _enrollments.IsEnrolledAsync(_candidate, _exam.Id, Arg.Any<CancellationToken>()).Returns(false);

        var error = await Assert.ThrowsAsync<ExamNotAvailableError>(() => Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
        _attempts.DidNotReceive().Add(Arg.Any<Attempt>());
    }

    [Fact]
    public async Task Start_ForAnUnknownExam_ReportsTheSameAnswerAsNotEnrolled()
    {
        await Assert.ThrowsAsync<ExamNotAvailableError>(() => Start.HandleAsync(Guid.NewGuid(), _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task Start_ForAnUnpublishedExam_ReportsTheSameAnswerAsNotEnrolled()
    {
        var draft = Fixtures.Exam([_q1], published: false);
        _catalog.FindAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _enrollments.IsEnrolledAsync(_candidate, draft.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<ExamNotAvailableError>(() => Start.HandleAsync(draft.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task Start_BeforeTheWindowOpens_IsRefused()
    {
        _clock.UtcNow = _exam.StartUtc.AddMinutes(-1);

        var error = await Assert.ThrowsAsync<ExamNotOpenError>(() => Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None));

        Assert.Equal("exam_not_open", error.ErrorCode);
    }

    [Fact]
    public async Task Start_AfterTheWindowCloses_IsRefused()
    {
        _clock.UtcNow = _exam.EndUtc.AddMinutes(1);

        var error = await Assert.ThrowsAsync<ExamClosedError>(() => Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None));

        Assert.Equal("exam_closed", error.ErrorCode);
    }

    [Fact]
    public async Task Start_AfterTheLateEntryDeadline_IsRefused()
    {
        var exam = Fixtures.Exam([_q1], lateEntry: Fixtures.Now.AddMinutes(-5));
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _enrollments.IsEnrolledAsync(_candidate, exam.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<ExamClosedError>(() => Start.HandleAsync(exam.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task Start_AtTheVeryEndOfTheWindow_IsRefusedBecauseNoTimeIsLeft()
    {
        _clock.UtcNow = _exam.EndUtc;

        await Assert.ThrowsAsync<ExamClosedError>(() => Start.HandleAsync(_exam.Id, _candidate, CancellationToken.None));
    }

    // ---- get ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Get_ForSomeoneElsesAttempt_LooksExactlyLikeAMissingOne()
    {
        var attempt = OpenAttempt();

        var error = await Assert.ThrowsAsync<AttemptNotFoundError>(() => Get.HandleAsync(attempt.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
    }

    [Fact]
    public async Task Get_ForAnUnknownAttempt_IsNotFound()
    {
        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Get.HandleAsync(Guid.NewGuid(), _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task Get_AfterTheDeadline_ClosesTheAttemptWithWhatWasSaved()
    {
        var attempt = OpenAttempt(startedAt: Fixtures.Now.AddMinutes(-31), deadline: Fixtures.Now.AddMinutes(-1));

        var dto = await Get.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, dto.Status);
        Assert.Equal(0m, dto.Score);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- save an answer ----------------------------------------------------------------------------------------

    [Fact]
    public async Task SaveAnswer_RecordsTheChoiceAndSaves()
    {
        var attempt = OpenAttempt();

        await Save.HandleAsync(attempt.Id, _candidate, _q1.Id, _q1.Correct(), CancellationToken.None);

        Assert.Equal(_q1.Correct(), Assert.Single(attempt.Answers).SelectedOptionId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAnswer_ForAQuestionThatIsNotInTheExam_IsRefused()
    {
        var attempt = OpenAttempt();
        var outsider = Fixtures.Question();

        var error = await Assert.ThrowsAsync<InvalidAnswerError>(
            () => Save.HandleAsync(attempt.Id, _candidate, outsider.Id, outsider.Correct(), CancellationToken.None));

        Assert.Equal(400, error.HttpStatusCode);
        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public async Task SaveAnswer_WithAnOptionFromAnotherQuestion_IsRefused()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<InvalidAnswerError>(
            () => Save.HandleAsync(attempt.Id, _candidate, _q1.Id, _q2.Correct(), CancellationToken.None));

        Assert.Empty(attempt.Answers);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAnswer_ForSomeoneElsesAttempt_IsNotFound()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => Save.HandleAsync(attempt.Id, Guid.NewGuid(), _q1.Id, _q1.Correct(), CancellationToken.None));
    }

    [Fact]
    public async Task SaveAnswer_AfterTheDeadline_ClosesTheAttemptAndRefusesTheAnswer()
    {
        var attempt = OpenAttempt(startedAt: Fixtures.Now.AddMinutes(-31), deadline: Fixtures.Now.AddMinutes(-1));

        await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => Save.HandleAsync(attempt.Id, _candidate, _q1.Id, _q1.Correct(), CancellationToken.None));

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.True(attempt.AutoSubmitted);
        Assert.Empty(attempt.Answers);
    }

    // ---- submit ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Submit_ScoresTheAttemptAgainstTheAnswerKey()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now);
        attempt.RecordAnswer(_q2.Id, _q2.Wrong(), Fixtures.Now);
        _clock.UtcNow = Fixtures.Now.AddMinutes(10);

        var dto = await Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(AttemptStatus.Submitted, dto.Status);
        Assert.Equal(1m, dto.Score);
        Assert.Equal(2m, dto.MaxScore);
        Assert.False(dto.AutoSubmitted);
        Assert.Empty(dto.Sections);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_Twice_ReturnsTheSameResultWithoutScoringAgain()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now);
        await Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        var again = await Submit.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal(1m, again.Score);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ForSomeoneElsesAttempt_IsNotFound()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Submit.HandleAsync(attempt.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
    }

    // ---- my exams ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task MyExams_ShowsTheCandidatesAttemptStateAlongsideTheWindow()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(5), 1m, 2m);
        _enrollments.GetEnrolledExamIdsAsync(_candidate, Arg.Any<CancellationToken>()).Returns([_exam.Id]);
        _catalog.FindPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_exam]);
        _attempts.ListForCandidateAsync(_candidate, Arg.Any<CancellationToken>()).Returns([attempt]);

        var exams = await new MyExamsHandler(_enrollments, _catalog, _attempts, _clock).HandleAsync(_candidate, CancellationToken.None);

        var item = Assert.Single(exams);
        Assert.Equal(attempt.Id, item.AttemptId);
        Assert.Equal(AttemptStatus.Submitted, item.AttemptStatus);
        Assert.Equal(1m, item.Score);
        Assert.Equal(2m, item.MaxScore);
        Assert.Equal(2, item.QuestionCount);
    }

    [Fact]
    public async Task MyExams_WithoutAnAttempt_HasNoAttemptFields()
    {
        _enrollments.GetEnrolledExamIdsAsync(_candidate, Arg.Any<CancellationToken>()).Returns([_exam.Id]);
        _catalog.FindPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_exam]);
        _attempts.ListForCandidateAsync(_candidate, Arg.Any<CancellationToken>()).Returns([]);

        var exams = await new MyExamsHandler(_enrollments, _catalog, _attempts, _clock).HandleAsync(_candidate, CancellationToken.None);

        var item = Assert.Single(exams);
        Assert.Null(item.AttemptId);
        Assert.Null(item.AttemptStatus);
        Assert.Null(item.Score);
    }
}
