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
    private readonly IExtraAttemptGrantRepository _grants = Substitute.For<IExtraAttemptGrantRepository>();
    private readonly List<Attempt> _theirs = [];
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

    private readonly QuestionSnapshot _q1 = Fixtures.Question("First");
    private readonly QuestionSnapshot _q2 = Fixtures.Question("Second");
    private readonly ExamSnapshot _exam;

    public AttemptHandlerTests()
    {
        _exam = Fixtures.Exam([_q1, _q2]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _enrollments.IsEnrolledAsync(_candidate, _exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        _attempts.ListForCandidateAtExamAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Attempt>>(_theirs.OrderBy(a => a.Number).ToList()));
        _grants.CountAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(0);
        _grants.CountsForCandidateAsync(_candidate, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _q1, _q2 }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private AttemptViewBuilder Views => new(_bank, _clock);
    private AttemptCloser Closer => new(_bank, _unitOfWork, _clock);
    private AttemptAccess Access => new(_attempts, _catalog, Closer, _clock);
    private StartAttemptHandler Start => new(_catalog, _enrollments, _attempts, _grants, _unitOfWork, Access, Views, _clock);
    private SaveAnswerHandler Save => new(Access, _bank, _unitOfWork, _clock);
    private ClearAnswerHandler Clear => new(Access, _unitOfWork, _clock);
    private MarkQuestionHandler Mark => new(Access, _unitOfWork, _clock);
    private MoveToSectionHandler MoveSection => new(Access, _unitOfWork, _clock);
    private SubmitAttemptHandler Submit => new(Access, Closer, Views);
    private GetAttemptHandler Get => new(Access, Views);

    private Attempt OpenAttempt(DateTime? startedAt = null, DateTime? deadline = null)
    {
        var started = startedAt ?? Fixtures.Now;
        var attempt = Attempt.Start(_exam.Id, _candidate, _theirs.Count + 1, started, deadline ?? started.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        _theirs.Add(attempt);
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

    // ---- clear a response --------------------------------------------------------------------------------------

    [Fact]
    public async Task ClearAnswer_RemovesTheSavedChoiceAndSaves()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now);
        attempt.RecordAnswer(_q2.Id, _q2.Wrong(), Fixtures.Now);

        await Clear.HandleAsync(attempt.Id, _candidate, _q1.Id, CancellationToken.None);

        Assert.Equal(_q2.Id, Assert.Single(attempt.Answers).QuestionId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearAnswer_ForAQuestionThatIsNotInTheExam_IsNotFound()
    {
        var attempt = OpenAttempt();
        var outsider = Fixtures.Question();

        var error = await Assert.ThrowsAsync<QuestionNotInAttemptError>(
            () => Clear.HandleAsync(attempt.Id, _candidate, outsider.Id, CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
        Assert.Equal("question_not_in_attempt", error.ErrorCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearAnswer_ForSomeoneElsesAttempt_IsNotFound_AndKeepsTheirAnswer()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now);

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => Clear.HandleAsync(attempt.Id, Guid.NewGuid(), _q1.Id, CancellationToken.None));

        Assert.Single(attempt.Answers);
    }

    [Fact]
    public async Task ClearAnswer_AfterTheDeadline_ClosesTheAttemptKeepingTheAnswerItWasScoredWith()
    {
        var attempt = OpenAttempt(startedAt: Fixtures.Now.AddMinutes(-31), deadline: Fixtures.Now.AddMinutes(-1));
        attempt.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now.AddMinutes(-20));

        await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => Clear.HandleAsync(attempt.Id, _candidate, _q1.Id, CancellationToken.None));

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(1m, attempt.Score);
        Assert.Single(attempt.Answers);
    }

    // ---- mark for review ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Mark_MarksTheQuestionAndSaves()
    {
        var attempt = OpenAttempt();

        await Mark.HandleAsync(attempt.Id, _candidate, _q1.Id, marked: true, CancellationToken.None);

        Assert.Equal(_q1.Id, Assert.Single(attempt.Marks).QuestionId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mark_Unmarking_TakesTheMarkOff()
    {
        var attempt = OpenAttempt();
        attempt.SetMarked(_q1.Id, true, Fixtures.Now);

        await Mark.HandleAsync(attempt.Id, _candidate, _q1.Id, marked: false, CancellationToken.None);

        Assert.Empty(attempt.Marks);
    }

    [Fact]
    public async Task Mark_ForAQuestionThatIsNotInTheExam_IsNotFound()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<QuestionNotInAttemptError>(
            () => Mark.HandleAsync(attempt.Id, _candidate, Guid.NewGuid(), marked: true, CancellationToken.None));

        Assert.Empty(attempt.Marks);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mark_ForSomeoneElsesAttempt_IsNotFound()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => Mark.HandleAsync(attempt.Id, Guid.NewGuid(), _q1.Id, marked: true, CancellationToken.None));

        Assert.Empty(attempt.Marks);
    }

    [Fact]
    public async Task Mark_AfterTheDeadline_ClosesTheAttemptAndRefusesTheMark()
    {
        var attempt = OpenAttempt(startedAt: Fixtures.Now.AddMinutes(-31), deadline: Fixtures.Now.AddMinutes(-1));

        await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => Mark.HandleAsync(attempt.Id, _candidate, _q1.Id, marked: true, CancellationToken.None));

        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Empty(attempt.Marks);
    }

    [Fact]
    public async Task Get_ShowsWhichQuestionsAreMarked_AlongsideTheirAnswers()
    {
        var attempt = OpenAttempt();
        attempt.RecordAnswer(_q1.Id, _q1.Correct(), Fixtures.Now);
        attempt.SetMarked(_q1.Id, true, Fixtures.Now);
        attempt.SetMarked(_q2.Id, true, Fixtures.Now);

        var dto = await Get.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        var questions = Assert.Single(dto.Sections).Questions.ToDictionary(q => q.Id);
        Assert.True(questions[_q1.Id].MarkedForReview);
        Assert.Equal(_q1.Correct(), questions[_q1.Id].SelectedOptionId);
        Assert.True(questions[_q2.Id].MarkedForReview);
        Assert.Null(questions[_q2.Id].SelectedOptionId);
    }

    [Fact]
    public async Task Get_ShowsAnUnmarkedQuestionAsNotMarked()
    {
        var attempt = OpenAttempt();

        var dto = await Get.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.All(Assert.Single(dto.Sections).Questions, q => Assert.False(q.MarkedForReview));
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

        var exams = await new MyExamsHandler(_enrollments, _catalog, _attempts, _grants, _clock).HandleAsync(_candidate, CancellationToken.None);

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

        var exams = await new MyExamsHandler(_enrollments, _catalog, _attempts, _grants, _clock).HandleAsync(_candidate, CancellationToken.None);

        var item = Assert.Single(exams);
        Assert.Null(item.AttemptId);
        Assert.Null(item.AttemptStatus);
        Assert.Null(item.Score);
    }

    // ---- section lock ------------------------------------------------------------------------------------------

    /// <summary>Makes the exam two sections, the first question in A and the second in B, with the lock on.</summary>
    private (ExamSnapshot Exam, ExamSectionSnapshot A, ExamSectionSnapshot B) LockedTwoSectionExam()
    {
        var a = new ExamSectionSnapshot(Guid.NewGuid(), "Section A", 1, [_q1.Id]);
        var b = new ExamSectionSnapshot(Guid.NewGuid(), "Section B", 2, [_q2.Id]);
        var locked = _exam with { Sections = [a, b], SectionLockEnabled = true };
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(locked);
        return (locked, a, b);
    }

    [Fact]
    public async Task SectionLock_RefusesAnAnswerMarkOrClearOutsideTheCurrentSection()
    {
        LockedTwoSectionExam();
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<SectionLockedError>(() => Save.HandleAsync(attempt.Id, _candidate, _q2.Id, _q2.Correct(), CancellationToken.None));
        await Assert.ThrowsAsync<SectionLockedError>(() => Mark.HandleAsync(attempt.Id, _candidate, _q2.Id, marked: true, CancellationToken.None));
        await Assert.ThrowsAsync<SectionLockedError>(() => Clear.HandleAsync(attempt.Id, _candidate, _q2.Id, CancellationToken.None));
        Assert.Empty(attempt.Answers);
        Assert.Empty(attempt.Marks);

        await Save.HandleAsync(attempt.Id, _candidate, _q1.Id, _q1.Correct(), CancellationToken.None);
        Assert.Single(attempt.Answers);
    }

    [Fact]
    public async Task SectionLock_MovingOnOpensTheNextSection_AndClosesTheOneLeft()
    {
        var (_, _, b) = LockedTwoSectionExam();
        var attempt = OpenAttempt();
        await Save.HandleAsync(attempt.Id, _candidate, _q1.Id, _q1.Correct(), CancellationToken.None);

        await MoveSection.HandleAsync(attempt.Id, _candidate, b.Id, CancellationToken.None);

        await Save.HandleAsync(attempt.Id, _candidate, _q2.Id, _q2.Correct(), CancellationToken.None);
        await Assert.ThrowsAsync<SectionLockedError>(() => Save.HandleAsync(attempt.Id, _candidate, _q1.Id, _q1.Wrong(), CancellationToken.None));
        Assert.Equal(2, attempt.Answers.Count);
    }

    [Fact]
    public async Task SectionLock_GoingBackToALeftSection_IsRefused()
    {
        var (_, a, b) = LockedTwoSectionExam();
        var attempt = OpenAttempt();
        await MoveSection.HandleAsync(attempt.Id, _candidate, b.Id, CancellationToken.None);

        await Assert.ThrowsAsync<SectionLockedError>(() => MoveSection.HandleAsync(attempt.Id, _candidate, a.Id, CancellationToken.None));
        Assert.Equal(2, attempt.ActiveSectionOrder);
    }

    [Fact]
    public async Task SectionLock_MovingToASectionThatIsNotInTheExam_IsNotFound()
    {
        LockedTwoSectionExam();
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<SectionNotInAttemptError>(() => MoveSection.HandleAsync(attempt.Id, _candidate, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task WithoutTheLock_EverySectionIsOpen_AndMovingChangesNothing()
    {
        var a = new ExamSectionSnapshot(Guid.NewGuid(), "Section A", 1, [_q1.Id]);
        var b = new ExamSectionSnapshot(Guid.NewGuid(), "Section B", 2, [_q2.Id]);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam with { Sections = [a, b] });
        var attempt = OpenAttempt();

        await Save.HandleAsync(attempt.Id, _candidate, _q2.Id, _q2.Correct(), CancellationToken.None);
        await MoveSection.HandleAsync(attempt.Id, _candidate, b.Id, CancellationToken.None);

        Assert.Equal(1, attempt.ActiveSectionOrder);
        var dto = await Get.HandleAsync(attempt.Id, _candidate, CancellationToken.None);
        Assert.False(dto.SectionLockEnabled);
        Assert.Null(dto.ActiveSectionId);
    }

    [Fact]
    public async Task Get_WhenSectionsAreLocked_NamesTheSectionTheCandidateIsIn()
    {
        var (_, a, b) = LockedTwoSectionExam();
        var attempt = OpenAttempt();

        var first = await Get.HandleAsync(attempt.Id, _candidate, CancellationToken.None);
        Assert.True(first.SectionLockEnabled);
        Assert.Equal(a.Id, first.ActiveSectionId);

        await MoveSection.HandleAsync(attempt.Id, _candidate, b.Id, CancellationToken.None);
        var second = await Get.HandleAsync(attempt.Id, _candidate, CancellationToken.None);
        Assert.Equal(b.Id, second.ActiveSectionId);
    }
}
