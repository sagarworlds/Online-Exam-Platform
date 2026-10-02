using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>When a draft exam's scope objects to one of its questions being filed somewhere else (FR-11).</summary>
public class ExamScopePlacementGuardTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Maths = Guid.NewGuid();
    private static readonly Guid Algebra = Guid.NewGuid();
    private static readonly Guid Geometry = Guid.NewGuid();
    private static readonly Guid Physics = Guid.NewGuid();
    private static readonly Guid Optics = Guid.NewGuid();

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly ExamScopePlacementGuard guard;

    public ExamScopePlacementGuardTests() => guard = new ExamScopePlacementGuard(repository);

    /// <summary>A draft exam limited to <paramref name="scope"/>, holding one question that is filed under Algebra.</summary>
    private (Exam Exam, Guid Question) DraftHolding(ExamScope scope)
    {
        var exam = new Exam(null, "Maths mock", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());
        exam.SetScope(scope, new Dictionary<Guid, QuestionPlacement>());
        var question = Guid.NewGuid();
        exam.AddQuestion(exam.AddSection("S", null).Id, question, new QuestionPlacement(Maths, Algebra));
        Serve(exam, question);
        return (exam, question);
    }

    private void Serve(Exam exam, Guid question)
    {
        repository.ListUsesOfQuestionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ExamQuestionUse(question, exam.Id, exam.Name, exam.Status)]);
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
    }

    [Fact]
    public async Task MovingOutOfAChapterWiseDraft_IsObjectedTo_NamingTheExam()
    {
        var (_, question) = DraftHolding(ExamScope.ForChapters(Maths, [Algebra]));

        var objections = await guard.CheckAsync([question], Maths, Geometry, CancellationToken.None);

        var objection = Assert.Single(objections);
        Assert.Equal(question, objection.QuestionId);
        Assert.Contains("Maths mock", objection.Reason);
    }

    [Fact]
    public async Task MovingWithinTheChosenChapters_IsFine()
    {
        var (_, question) = DraftHolding(ExamScope.ForChapters(Maths, [Algebra, Geometry]));

        Assert.Empty(await guard.CheckAsync([question], Maths, Geometry, CancellationToken.None));
    }

    [Fact]
    public async Task MovingToAnotherBook_OutOfAWholeBookDraft_IsObjectedTo_ButToAnotherChapterOfTheBookIsFine()
    {
        var (_, question) = DraftHolding(ExamScope.ForBook(Maths));

        Assert.Empty(await guard.CheckAsync([question], Maths, Geometry, CancellationToken.None));
        Assert.Single(await guard.CheckAsync([question], Physics, Optics, CancellationToken.None));
    }

    [Fact]
    public async Task AnIndependentDraft_NeverObjects()
    {
        var (_, question) = DraftHolding(ExamScope.Independent());

        Assert.Empty(await guard.CheckAsync([question], Physics, Optics, CancellationToken.None));
    }

    [Fact]
    public async Task APublishedExam_NeverObjects_BecauseItsScopeIsFixedAndDeliveryDoesNotReadPlacement()
    {
        var question = Guid.NewGuid();
        var exam = new Exam(null, "Published", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());
        exam.SetScope(ExamScope.ForChapters(Maths, [Algebra]), new Dictionary<Guid, QuestionPlacement>());
        exam.AddQuestion(exam.AddSection("S", null).Id, question, new QuestionPlacement(Maths, Algebra));
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.Publish(Now);
        Serve(exam, question);

        Assert.Empty(await guard.CheckAsync([question], Physics, Optics, CancellationToken.None));
        await repository.DidNotReceiveWithAnyArgs().ListByIdsAsync(default!, default);
    }

    [Fact]
    public async Task AQuestionInNoExam_NeverObjects()
    {
        repository.ListUsesOfQuestionsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await guard.CheckAsync([Guid.NewGuid()], Physics, Optics, CancellationToken.None));
    }
}
