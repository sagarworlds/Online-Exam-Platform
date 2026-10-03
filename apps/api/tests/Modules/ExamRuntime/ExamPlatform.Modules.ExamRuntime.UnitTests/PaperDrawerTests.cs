using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Making up a candidate's paper: fixed questions always appear, rules add more, and a short pool refuses the start.</summary>
public class PaperDrawerTests
{
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly PaperDrawer _drawer;

    public PaperDrawerTests()
    {
        _drawer = new PaperDrawer(_bank, new RandomQuestionPicker());
    }

    private void BankHolds(params Guid[] ids) =>
        _bank.FindAsync(Arg.Any<QuestionCriteria>(), Arg.Any<CancellationToken>())
            .Returns(ids.Select(id => new FoundQuestion(id, null, null)).ToList());

    private static ExamSnapshot ExamWith(Guid[] fixedIds, int drawCount)
    {
        var exam = Fixtures.Exam([]);
        return exam with
        {
            Sections = [new ExamSectionSnapshot(Guid.NewGuid(), "A", 1, fixedIds, [new DrawRuleSnapshot(drawCount, null, null, null, null, null)])],
        };
    }

    [Fact]
    public async Task KeepsTheFixedQuestions_AndAddsDrawnOnesThatAreNotAlreadyOnThePaper()
    {
        var fixedId = Guid.NewGuid();
        var pool = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        BankHolds([fixedId, .. pool]);
        var exam = ExamWith([fixedId], 3);

        var paper = await _drawer.DrawAsync(exam, CancellationToken.None);

        Assert.Equal(4, paper.Count);
        Assert.Equal(fixedId, paper[0].QuestionId);
        Assert.Equal(4, paper.Select(p => p.QuestionId).Distinct().Count());
        Assert.All(paper.Skip(1), p => Assert.Contains(p.QuestionId, pool));
    }

    [Fact]
    public async Task RefusesToStart_WhenThePoolIsShort()
    {
        BankHolds(Guid.NewGuid(), Guid.NewGuid());

        await Assert.ThrowsAsync<PaperCannotBeDrawnError>(() => _drawer.DrawAsync(ExamWith([], 3), CancellationToken.None));
    }

    [Fact]
    public async Task EachCandidateGetsAnIndependentDraw()
    {
        BankHolds(Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray());
        var exam = ExamWith([], 10);

        var first = (await _drawer.DrawAsync(exam, CancellationToken.None)).Select(p => p.QuestionId);
        var second = (await _drawer.DrawAsync(exam, CancellationToken.None)).Select(p => p.QuestionId);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ThePaperReplacesTheExamsQuestionsForThatAttemptOnly()
    {
        var exam = ExamWith([], 1);
        var section = exam.Sections[0];
        var attempt = Attempt.Start(exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        var drawn = Guid.NewGuid();
        attempt.SetPaper([(section.Id, drawn)]);

        var seen = exam.For(attempt);

        Assert.Equal([drawn], seen.Sections[0].QuestionIds);
        Assert.Empty(exam.Sections[0].QuestionIds);
    }

    [Fact]
    public void SetPaper_RefusesARepeatedQuestion()
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        var id = Guid.NewGuid();

        Assert.Throws<InvalidAttemptError>(() => attempt.SetPaper([(Guid.NewGuid(), id), (Guid.NewGuid(), id)]));
    }
}
