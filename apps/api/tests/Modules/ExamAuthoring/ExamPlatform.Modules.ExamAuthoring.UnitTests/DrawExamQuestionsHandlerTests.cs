using ExamPlatform.SharedKernel.Application;
using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>Drawing a random selection of bank questions into a section: what is eligible, and that it is all or nothing.</summary>
public class DrawExamQuestionsHandlerTests
{
    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly IQuestionBank bank = Substitute.For<IQuestionBank>();
    private readonly IQuestionPicker picker = Substitute.For<IQuestionPicker>();
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());
    private readonly ExamSection section;
    private readonly DrawExamQuestionsHandler handler;

    public DrawExamQuestionsHandlerTests()
    {
        section = exam.AddSection("S", null);
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        // The first ones, so a test knows which were drawn.
        picker.Pick(Arg.Any<IReadOnlyList<FoundQuestion>>(), Arg.Any<int>())
            .Returns(call => call.Arg<IReadOnlyList<FoundQuestion>>().Take(call.Arg<int>()).ToList());
        bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().Select(id => new QuestionSnapshot(id, $"text {id}", [])).ToList());
        handler = new DrawExamQuestionsHandler(repository, unitOfWork, bank, picker);
    }

    private void BankHolds(params FoundQuestion[] found) =>
        bank.FindAsync(Arg.Any<QuestionCriteria>(), Arg.Any<CancellationToken>()).Returns(found);

    private static FoundQuestion Unfiled() => new(Guid.NewGuid(), null, null);

    [Fact]
    public async Task AddsTheDrawnQuestionsToTheSection_AndSavesOnce()
    {
        var found = new[] { Unfiled(), Unfiled(), Unfiled() };
        BankHolds(found);

        var added = await handler.HandleAsync(new DrawExamQuestionsCommand(exam.Id, section.Id, 2), CancellationToken.None);

        Assert.Equal([found[0].Id, found[1].Id], section.Questions.Select(q => q.QuestionVersionId));
        Assert.Equal([found[0].Id, found[1].Id], added.Select(q => q.QuestionId));
        Assert.Equal([1, 2], added.Select(q => q.Order));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PassesTheCriteriaToTheBank()
    {
        BankHolds(Unfiled());
        var book = Guid.NewGuid();
        var chapter = Guid.NewGuid();

        await handler.HandleAsync(new DrawExamQuestionsCommand(exam.Id, section.Id, 1, book, chapter, "hard", "fractions"), CancellationToken.None);

        await bank.Received(1).FindAsync(new QuestionCriteria(book, chapter, "hard", "fractions"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionsAlreadyInTheExam_AreNotDrawnAgain()
    {
        var already = Unfiled();
        var fresh = Unfiled();
        exam.AddQuestion(section.Id, already.Id, QuestionPlacement.Unfiled);
        BankHolds(already, fresh);

        var added = await handler.HandleAsync(new DrawExamQuestionsCommand(exam.Id, section.Id, 1), CancellationToken.None);

        Assert.Equal([fresh.Id], added.Select(q => q.QuestionId));
    }

    [Fact]
    public async Task QuestionsOutsideTheExamsScope_AreNotDrawn()
    {
        var book = Guid.NewGuid();
        exam.SetScope(ExamScope.ForBook(book), new Dictionary<Guid, QuestionPlacement>());
        var inside = new FoundQuestion(Guid.NewGuid(), Guid.NewGuid(), book);
        var outside = new FoundQuestion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        BankHolds(outside, inside);

        var added = await handler.HandleAsync(new DrawExamQuestionsCommand(exam.Id, section.Id, 1), CancellationToken.None);

        Assert.Equal([inside.Id], added.Select(q => q.QuestionId));
    }

    [Fact]
    public async Task WhenFewerAreAvailableThanAskedFor_NothingIsAdded_AndTheErrorSaysHowMany()
    {
        BankHolds(Unfiled(), Unfiled());

        var error = await Assert.ThrowsAsync<NotEnoughQuestionsError>(() =>
            handler.HandleAsync(new DrawExamQuestionsCommand(exam.Id, section.Id, 3), CancellationToken.None));

        Assert.Contains("only 2", error.Message);
        Assert.Empty(section.Questions);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(DrawExamQuestionsHandler.MaxCount + 1)]
    public async Task ACountOutsideTheAllowedRange_IsRefusedBeforeAnythingIsRead(int count)
    {
        await Assert.ThrowsAsync<InvalidExamConfigError>(() =>
            handler.HandleAsync(new DrawExamQuestionsCommand(exam.Id, section.Id, count), CancellationToken.None));

        await bank.DidNotReceive().FindAsync(Arg.Any<QuestionCriteria>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAnUnknownSection_ThrowsAndAsksTheBankNothing()
    {
        await Assert.ThrowsAsync<SectionNotFoundError>(() =>
            handler.HandleAsync(new DrawExamQuestionsCommand(exam.Id, Guid.NewGuid(), 1), CancellationToken.None));

        await bank.DidNotReceive().FindAsync(Arg.Any<QuestionCriteria>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheRandomPicker_ReturnsTheCountAskedFor_EachItemAtMostOnce()
    {
        var items = Enumerable.Range(0, 20).ToList();

        var picked = new RandomQuestionPicker().Pick(items, 7);

        Assert.Equal(7, picked.Count);
        Assert.Equal(7, picked.Distinct().Count());
        Assert.All(picked, item => Assert.Contains(item, items));
    }

    [Fact]
    public void TheRandomPicker_CanTakeEverything()
    {
        var picked = new RandomQuestionPicker().Pick([1, 2, 3], 3);

        Assert.Equal([1, 2, 3], picked.OrderBy(i => i));
    }
}
