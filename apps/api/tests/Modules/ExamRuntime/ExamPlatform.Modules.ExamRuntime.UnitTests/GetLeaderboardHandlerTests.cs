using ExamPlatform.Modules.Batch.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// The leaderboards (FR-35): who may see a board, when, which results count, and which candidates sit on each board. The ranking and the
/// name rules are pinned in <see cref="LeaderboardRulesTests"/>.
/// </summary>
public class GetLeaderboardHandlerTests
{
    private readonly Guid _me = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IEnrollments _enrollments = Substitute.For<IEnrollments>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamBatchMembers _batches = Substitute.For<IExamBatchMembers>();
    private readonly IDisplayNameDirectory _names = Substitute.For<IDisplayNameDirectory>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IQuestionSubjects _subjects = Substitute.For<IQuestionSubjects>();

    private ExamSnapshot _exam = null!;

    private GetLeaderboardHandler Handler => new(_catalog, _enrollments, _attempts, _batches, _names, new SubjectMarks(_bank, _subjects), _clock);

    public GetLeaderboardHandlerTests()
    {
        _exam = Fixtures.Exam([], correct: 4m, incorrect: -1m, unattempted: 0m);
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _enrollments.IsEnrolledAsync(_me, _exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        _batches.ListMembersOfExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BatchMemberRef>>([]));
        _attempts.ListSubmittedScoresAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SubmittedScore>>([]));
        _names.GetDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>()));
    }

    /// <summary>Makes the exam the one the test is about, enrolled for the signed-in candidate, so the catalog and enrolment agree.</summary>
    private void UseExam(ExamSnapshot exam)
    {
        _exam = exam;
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _enrollments.IsEnrolledAsync(_me, exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        _batches.ListMembersOfExamAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BatchMemberRef>>([]));
    }

    private void Scored(params (Guid Candidate, decimal Score)[] scores) =>
        _attempts.ListSubmittedScoresAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SubmittedScore>>(
            scores.Select(s => new SubmittedScore(Guid.NewGuid(), s.Candidate, s.Score)).ToList()));

    private void Names(IReadOnlyDictionary<Guid, string> names) =>
        _names.GetDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(names));

    private Task<LeaderboardDto> BoardAsync(string? board = null, Guid? batchId = null, string? subject = null) =>
        Handler.HandleAsync(_exam.Id, _me, board, batchId, subject, CancellationToken.None);

    [Fact]
    public async Task TheOverallBoard_RanksEveryCandidate_AndMarksTheCandidatesOwnRow()
    {
        var other = Guid.NewGuid();
        Scored((other, 9m), (_me, 5m));
        Names(new Dictionary<Guid, string> { [other] = "Ravi Shah", [_me] = "Asha Kumar" });

        var board = await BoardAsync();

        Assert.Equal("overall", board.Board);
        Assert.Equal(2, board.CandidateCount);
        Assert.Equal([1, 2], board.Entries.Select(e => e.Rank));
        Assert.Equal(["Ravi S.", "Asha K."], board.Entries.Select(e => e.Name));
        Assert.Equal([false, true], board.Entries.Select(e => e.IsYou));
        Assert.Null(board.You);
        Assert.False(board.Truncated);
    }

    [Fact]
    public async Task ACandidateWithARetake_CountsOnce_ByTheirBestAttempt()
    {
        var other = Guid.NewGuid();
        _attempts.ListSubmittedScoresAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SubmittedScore>>(
        [
            new SubmittedScore(Guid.NewGuid(), _me, 3m),
            new SubmittedScore(Guid.NewGuid(), _me, 9m),
            new SubmittedScore(Guid.NewGuid(), other, 6m),
        ]));

        var board = await BoardAsync();

        Assert.Equal(2, board.CandidateCount);
        Assert.True(board.Entries[0].IsYou, "the candidate's best attempt ranks them first");
        Assert.False(board.Entries[1].IsYou);
        Assert.Equal(9m, board.Entries[0].Score);
    }

    [Fact]
    public async Task ACandidate_WithoutAResult_IsNotOnTheBoard_AndHasNoRow()
    {
        Scored((Guid.NewGuid(), 4m));

        var board = await BoardAsync();

        Assert.Equal(1, board.CandidateCount);
        Assert.Null(board.You);
        Assert.DoesNotContain(board.Entries, e => e.IsYou);
    }

    [Fact]
    public async Task BeforeTheResultsAreReleased_NoBoardIsShown()
    {
        UseExam(Fixtures.Exam([], resultRelease: ExamResultReleaseMode.Scheduled, resultReleaseTime: Fixtures.Now.AddDays(1)));

        await Assert.ThrowsAsync<ResultsNotReleasedError>(() => BoardAsync());
    }

    [Fact]
    public async Task ACandidateNotEnrolledInTheExam_SeesNoBoard()
    {
        _enrollments.IsEnrolledAsync(_me, _exam.Id, Arg.Any<CancellationToken>()).Returns(false);

        await Assert.ThrowsAsync<CandidateNotEnrolledError>(() => BoardAsync());
    }

    [Fact]
    public async Task AnUnknownBoard_IsRefused()
    {
        await Assert.ThrowsAsync<InvalidLeaderboardBoardError>(() => BoardAsync("everyone"));
    }

    [Fact]
    public async Task TheBatchBoard_ListsOnlyTheMembersOfTheBatchShown()
    {
        var batch = Guid.NewGuid();
        var classmate = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        _batches.ListMembersOfExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BatchMemberRef>>(
        [
            new BatchMemberRef(_me, batch, "Morning"),
            new BatchMemberRef(classmate, batch, "Morning"),
        ]));
        Scored((outsider, 10m), (classmate, 6m), (_me, 4m));

        var board = await BoardAsync("batch");

        Assert.Equal(batch, board.BatchId);
        Assert.Equal(2, board.CandidateCount);
        Assert.Equal([1, 2], board.Entries.Select(e => e.Rank));
        Assert.Equal([new LeaderboardBatchDto(batch, "Morning")], board.Batches);
    }

    [Fact]
    public async Task ABatchTheCandidateIsNotIn_IsRefused_AsNotFound()
    {
        _batches.ListMembersOfExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BatchMemberRef>>(
            [new BatchMemberRef(Guid.NewGuid(), Guid.NewGuid(), "Evening")]));

        await Assert.ThrowsAsync<LeaderboardBatchNotFoundError>(() => BoardAsync("batch", batchId: Guid.NewGuid()));
    }

    [Fact]
    public async Task TheSubjectBoard_RanksEachCandidateByTheirMarksInThatSubject()
    {
        var maths = Fixtures.Question("2 + 2?");
        var physics = Fixtures.Question("Force?");
        UseExam(Fixtures.Exam([maths, physics], correct: 4m, incorrect: -1m, unattempted: 0m));
        _subjects.GetSubjectsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [maths.Id] = "Maths", [physics.Id] = "Physics" }));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([maths, physics]));

        var mine = Attempt.Start(_exam.Id, _me, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        mine.RecordAnswer(maths.Id, maths.Correct(), Fixtures.Now);
        mine.RecordAnswer(physics.Id, physics.Wrong(), Fixtures.Now);
        mine.Submit(Fixtures.Now.AddMinutes(5), 3m, 8m);
        _attempts.ListSubmittedScoresAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SubmittedScore>>(
            [new SubmittedScore(mine.Id, _me, 3m)]));
        _attempts.ListWithAnswersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Attempt>>([mine]));

        var board = await BoardAsync("subject");

        Assert.Equal("subject", board.Board);
        Assert.Equal(["Maths", "Physics"], board.Subjects);
        Assert.Equal("Maths", board.Subject);
        // Maths was answered correctly for 4 marks; Physics is not the subject shown, so it does not count here.
        Assert.Equal(4m, board.Entries.Single().Score);
    }

    [Fact]
    public async Task ASubjectTheExamDoesNotHave_IsRefused_AsNotFound()
    {
        var maths = Fixtures.Question("2 + 2?");
        UseExam(Fixtures.Exam([maths], correct: 4m, incorrect: -1m, unattempted: 0m));
        _subjects.GetSubjectsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [maths.Id] = "Maths" }));
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([maths]));
        var mine = Attempt.Start(_exam.Id, _me, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        mine.Submit(Fixtures.Now.AddMinutes(5), 0m, 4m);
        _attempts.ListSubmittedScoresAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SubmittedScore>>(
            [new SubmittedScore(mine.Id, _me, 0m)]));
        _attempts.ListWithAnswersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Attempt>>([mine]));

        await Assert.ThrowsAsync<LeaderboardSubjectNotFoundError>(() => BoardAsync("subject", subject: "Physics"));
    }
}
