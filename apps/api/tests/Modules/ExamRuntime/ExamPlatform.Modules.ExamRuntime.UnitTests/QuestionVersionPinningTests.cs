using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>An attempt reads its questions at the version it began with (FR-7).</summary>
public class QuestionVersionPinningTests
{
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();

    private static Attempt NewAttempt() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddHours(1));

    [Fact]
    public void AnAttemptRemembersTheVersionItWasGiven_AndNothingForAQuestionItWasNotGiven()
    {
        var attempt = NewAttempt();
        var questionId = Guid.NewGuid();

        attempt.PinQuestionVersions(new Dictionary<Guid, int> { [questionId] = 3 });

        Assert.Equal(3, attempt.QuestionVersionOf(questionId));
        Assert.Null(attempt.QuestionVersionOf(Guid.NewGuid()));
    }

    [Fact]
    public void TheVersionsAreFixedOnce()
    {
        var attempt = NewAttempt();
        attempt.PinQuestionVersions(new Dictionary<Guid, int> { [Guid.NewGuid()] = 1 });

        Assert.Throws<InvalidAttemptError>(() => attempt.PinQuestionVersions(new Dictionary<Guid, int> { [Guid.NewGuid()] = 2 }));
    }

    [Fact]
    public void RepinningMovesOnlyThatQuestion()
    {
        var attempt = NewAttempt();
        var corrected = Guid.NewGuid();
        var other = Guid.NewGuid();
        attempt.PinQuestionVersions(new Dictionary<Guid, int> { [corrected] = 1, [other] = 2 });

        attempt.RepinQuestion(corrected, 4);

        Assert.Equal(4, attempt.QuestionVersionOf(corrected));
        Assert.Equal(2, attempt.QuestionVersionOf(other));
    }

    [Fact]
    public async Task AnAttemptWithVersions_ReadsEachQuestionAtItsVersion_AndOneWithoutReadsTheCurrentContent()
    {
        var pinned = Fixtures.Question("Version two wording");
        var unpinned = Fixtures.Question("Whatever it is now");
        var attempt = NewAttempt();
        attempt.PinQuestionVersions(new Dictionary<Guid, int> { [pinned.Id] = 2 });
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>()).Returns([pinned with { VersionNumber = 2 }, unpinned]);

        var read = await _bank.ReadAsync(attempt, [pinned.Id, unpinned.Id], CancellationToken.None);

        Assert.Equal(2, read[pinned.Id].VersionNumber);
        await _bank.Received(1).GetVersionsAsync(
            Arg.Is<IReadOnlyCollection<QuestionVersionRef>>(r => r.Single(x => x.QuestionId == pinned.Id).VersionNumber == 2 && r.Single(x => x.QuestionId == unpinned.Id).VersionNumber == null),
            Arg.Any<CancellationToken>());
        await _bank.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }

    [Fact]
    public async Task AnAttemptMadeBeforeVersionsWereKept_ReadsTheCurrentContent()
    {
        var question = Fixtures.Question();
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([question]);

        var read = await _bank.ReadAsync(NewAttempt(), [question.Id], CancellationToken.None);

        Assert.Same(question, read[question.Id]);
        await _bank.DidNotReceiveWithAnyArgs().GetVersionsAsync(default!, default);
    }

    [Fact]
    public async Task ACorrectedKey_MovesEachAttemptsQuestionToTheNewVersion_AndMarksItUnderThatKey()
    {
        // Version 1 marked the wrong option correct; version 2 is the corrected key.
        var v1 = Fixtures.Question();
        var chosen = v1.Wrong();
        var v2 = v1 with { VersionNumber = 2, Options = v1.Options.Select(o => o with { IsCorrect = o.Id == chosen }).ToList() };
        var exam = Fixtures.Exam([v1]);
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now.AddHours(-1), Fixtures.Now.AddHours(1));
        attempt.PinQuestionVersions(new Dictionary<Guid, int> { [v1.Id] = 1 });
        attempt.RecordAnswer(v1.Id, chosen, Fixtures.Now.AddMinutes(-50));
        attempt.Submit(Fixtures.Now.AddMinutes(-40), 0, 1);

        var attempts = Substitute.For<IAttemptRepository>();
        attempts.ListSubmittedByQuestionIdAsync(v1.Id, Arg.Any<CancellationToken>()).Returns([attempt]);
        var catalog = Substitute.For<IExamCatalog>();
        catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([v2]);
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<QuestionVersionRef>>().Single().VersionNumber == 2 ? [v2] : [v1]);
        var unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();

        var changed = await new AttemptRescorer(attempts, catalog, _bank, unitOfWork, new FakeClock(Fixtures.Now))
            .RescoreForQuestionAsync(v1.Id, "Key corrected", CancellationToken.None);

        Assert.Equal(1, changed);
        Assert.Equal(2, attempt.QuestionVersionOf(v1.Id));
        Assert.Equal(1, attempt.Score);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
