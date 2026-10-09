using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>ExamRuntime's side of an answer-key correction (FR-31): finding and rescoring every attempt it affects.</summary>
public class AttemptRescorerTests
{
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IDisputeRepository _disputes = Substitute.For<IDisputeRepository>();
    private readonly IRequestContext _requestContext = Substitute.For<IRequestContext>();
    private readonly Guid _staff = Guid.NewGuid();
    private readonly AttemptRescorer _rescorer;

    public AttemptRescorerTests()
    {
        _disputes.ListOpenForQuestionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _requestContext.UserId.Returns(_staff);
        _rescorer = new AttemptRescorer(_attempts, _catalog, _bank, _disputes, _unitOfWork, _requestContext, _clock);
    }

    private static Attempt SubmittedAttempt(Guid examId, QuestionSnapshot question, Guid chosenOption, decimal score, decimal maxScore)
    {
        var attempt = Attempt.Start(examId, Guid.NewGuid(), 1, Fixtures.Now.AddHours(-1), Fixtures.Now.AddHours(1));
        attempt.RecordAnswer(question.Id, chosenOption, Fixtures.Now.AddMinutes(-50));
        attempt.Submit(Fixtures.Now.AddMinutes(-40), score, maxScore);
        return attempt;
    }

    [Fact]
    public async Task RescoreForQuestionAsync_WithNoAffectedAttempts_DoesNothing()
    {
        var questionId = Guid.NewGuid();
        _attempts.ListSubmittedByQuestionIdAsync(questionId, Arg.Any<CancellationToken>()).Returns([]);

        var changed = await _rescorer.RescoreForQuestionAsync(questionId, "Corrected", CancellationToken.None);

        Assert.Equal(0, changed);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task RescoreForQuestionAsync_WhenTheCorrectedAnswerChangesTheScore_RevisesTheAttemptAndSaves()
    {
        // The candidate chose the option that used to be wrong; the key correction makes it the right one.
        var question = Fixtures.Question();
        var wronglyMarkedOption = question.Wrong();
        var exam = Fixtures.Exam([question]);
        // As originally scored: the candidate's choice was marked wrong, so they scored 0 of 1.
        var attempt = SubmittedAttempt(exam.Id, question, wronglyMarkedOption, score: 0, maxScore: 1);

        _attempts.ListSubmittedByQuestionIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        // The bank now reports the corrected key: the option the candidate chose is the correct one.
        var corrected = question with { Options = question.Options.Select(o => o with { IsCorrect = o.Id == wronglyMarkedOption }).ToList() };
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([corrected]);

        var changed = await _rescorer.RescoreForQuestionAsync(question.Id, "The marked option was wrong", CancellationToken.None);

        Assert.Equal(1, changed);
        Assert.Equal(1, attempt.Score);
        var revision = Assert.Single(attempt.Revisions);
        Assert.Equal(0, revision.PreviousScore);
        Assert.Equal(1, revision.NewScore);
        Assert.Equal("The marked option was wrong", revision.Reason);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescoreForQuestionAsync_WhenTheRescoredScoreIsTheSame_ChangesNothing_AndDoesNotSave()
    {
        var question = Fixtures.Question();
        var exam = Fixtures.Exam([question]);
        var attempt = SubmittedAttempt(exam.Id, question, question.Correct(), score: 1, maxScore: 1);

        _attempts.ListSubmittedByQuestionIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        // Unchanged key: the candidate's answer scores the same as it did originally.
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([question]);

        var changed = await _rescorer.RescoreForQuestionAsync(question.Id, "Double-checked, no change", CancellationToken.None);

        Assert.Equal(0, changed);
        Assert.Empty(attempt.Revisions);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task RescoreForQuestionAsync_AcceptsTheOpenDisputesOfTheQuestion_AndSavesEvenWhenNoScoreMoved()
    {
        // The candidate disputed a key that, once corrected, still scores them the same: the dispute is still answered by the correction.
        var question = Fixtures.Question();
        var exam = Fixtures.Exam([question]);
        var attempt = SubmittedAttempt(exam.Id, question, question.Correct(), score: 1, maxScore: 1);
        var dispute = Dispute.Raise(attempt.Id, exam.Id, attempt.CandidateId, question.Id, "Option 22 is also right", Fixtures.Now.AddMinutes(-5));

        _attempts.ListSubmittedByQuestionIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([question]);
        _disputes.ListOpenForQuestionAsync(question.Id, Arg.Any<CancellationToken>()).Returns([dispute]);

        var changed = await _rescorer.RescoreForQuestionAsync(question.Id, "Both options are right", CancellationToken.None);

        Assert.Equal(0, changed);
        Assert.Equal(DisputeStatus.Accepted, dispute.Status);
        Assert.Equal("Both options are right", dispute.ResolutionNote);
        Assert.Equal(_staff, dispute.ResolvedByUserId);
        Assert.Equal(Fixtures.Now, dispute.ResolvedAtUtc);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescoreForQuestionAsync_OutsideASignedInRequest_StillAcceptsTheDisputes_WithNoStaffUserNamed()
    {
        var question = Fixtures.Question();
        var exam = Fixtures.Exam([question]);
        var attempt = SubmittedAttempt(exam.Id, question, question.Correct(), score: 1, maxScore: 1);
        var dispute = Dispute.Raise(attempt.Id, exam.Id, attempt.CandidateId, question.Id, "Wrong key", Fixtures.Now.AddMinutes(-5));
        _requestContext.UserId.Returns((Guid?)null);

        _attempts.ListSubmittedByQuestionIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([question]);
        _disputes.ListOpenForQuestionAsync(question.Id, Arg.Any<CancellationToken>()).Returns([dispute]);

        await _rescorer.RescoreForQuestionAsync(question.Id, "Corrected", CancellationToken.None);

        Assert.Equal(DisputeStatus.Accepted, dispute.Status);
        Assert.Null(dispute.ResolvedByUserId);
    }

    [Fact]
    public async Task RescoreForQuestionAsync_WhenTheExamCanNoLongerBeRead_SkipsItsAttempts_RatherThanFailingTheWholeBatch()
    {
        var question = Fixtures.Question();
        var missingExamId = Guid.NewGuid();
        var attempt = SubmittedAttempt(missingExamId, question, question.Correct(), score: 1, maxScore: 1);

        _attempts.ListSubmittedByQuestionIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        _catalog.FindAsync(missingExamId, Arg.Any<CancellationToken>()).Returns((ExamSnapshot?)null);

        var changed = await _rescorer.RescoreForQuestionAsync(question.Id, "Corrected", CancellationToken.None);

        Assert.Equal(0, changed);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
