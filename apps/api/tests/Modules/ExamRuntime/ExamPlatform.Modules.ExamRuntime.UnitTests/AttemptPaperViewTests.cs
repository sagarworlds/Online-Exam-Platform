using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>What staff see when they open the paper of an attempt.</summary>
public class AttemptPaperViewTests
{
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly QuestionSnapshot _fixed = Fixtures.Question("Fixed one");
    private readonly QuestionSnapshot _drawn = Fixtures.Question("Drawn one");
    private readonly ExamSnapshot _exam;

    public AttemptPaperViewTests()
    {
        var baseExam = Fixtures.Exam([_fixed]);
        _exam = baseExam with
        {
            Sections = [baseExam.Sections[0] with { DrawRules = [new DrawRuleSnapshot(1, null, null, null, null, null)] }],
        };
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>()
                .Select(id => new[] { _fixed, _drawn }.FirstOrDefault(q => q.Id == id))
                .OfType<QuestionSnapshot>().ToList());
    }

    private GetAttemptPaperHandler Handler => new(_catalog, _attempts, _bank);

    private Attempt AttemptWithPaper(Guid examId)
    {
        var attempt = Attempt.Start(examId, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.SetPaper([(_exam.Sections[0].Id, _fixed.Id), (_exam.Sections[0].Id, _drawn.Id)]);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    [Fact]
    public async Task ShowsThePaperDrawnForTheAttempt_AndWhichQuestionsWereDrawn()
    {
        var attempt = AttemptWithPaper(_exam.Id);

        var paper = await Handler.HandleAsync(_exam.Id, attempt.Id, CancellationToken.None);

        Assert.True(paper.HasDrawnQuestions);
        var questions = Assert.Single(paper.Sections).Questions;
        Assert.Equal([(_fixed.Id, false), (_drawn.Id, true)], questions.Select(q => (q.Id, q.Drawn)));
        Assert.Equal("Drawn one", questions[1].Text);
    }

    [Fact]
    public async Task AnAttemptAtAnotherExam_IsNotFound()
    {
        var attempt = AttemptWithPaper(Guid.NewGuid());

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(_exam.Id, attempt.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownAttempt_IsNotFound() =>
        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(_exam.Id, Guid.NewGuid(), CancellationToken.None));
}
