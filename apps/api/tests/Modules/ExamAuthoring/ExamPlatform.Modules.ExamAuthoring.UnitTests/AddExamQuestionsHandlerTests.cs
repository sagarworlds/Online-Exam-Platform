using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>Adding several bank questions to a section in one call: their order, and that one refusal adds none of them.</summary>
public class AddExamQuestionsHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly IQuestionBank bank = Substitute.For<IQuestionBank>();
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid(), Now);
    private readonly ExamSection section;
    private readonly Dictionary<Guid, QuestionSnapshot> inBank = [];
    private readonly AddExamQuestionsHandler handler;

    public AddExamQuestionsHandlerTests()
    {
        section = exam.AddSection("S", null, Now);
        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        // Answers in the order of the bank's own choosing (here, reversed), as a real bank may: the handler must not lean on it.
        bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<Guid>>().Where(inBank.ContainsKey).Reverse().Select(id => inBank[id]).ToList());
        handler = new AddExamQuestionsHandler(repository, unitOfWork, bank, TestClock.At(Now));
    }

    private Guid InBank(Guid? bookId = null, Guid? chapterId = null, string? unusableReason = null)
    {
        var id = Guid.NewGuid();
        inBank[id] = new QuestionSnapshot(id, $"text {id}", [], chapterId, bookId, UnusableReason: unusableReason);
        return id;
    }

    private Task<IReadOnlyList<ExamQuestionDto>> AddAsync(params Guid[] ids) =>
        handler.HandleAsync(new AddExamQuestionsCommand(exam.Id, section.Id, ids), CancellationToken.None);

    [Fact]
    public async Task AddsEveryQuestionInTheOrderGiven_AndSavesOnce()
    {
        var first = InBank();
        var second = InBank();
        var third = InBank();

        var added = await AddAsync(second, third, first);

        Assert.Equal([second, third, first], section.Questions.OrderBy(q => q.Order).Select(q => q.QuestionVersionId));
        Assert.Equal([second, third, first], added.Select(q => q.QuestionId));
        Assert.Equal([1, 2, 3], added.Select(q => q.Order));
        Assert.Equal(inBank[second].Text, added[0].Text);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuestionsGoAfterTheOnesTheSectionAlreadyHolds()
    {
        var held = InBank();
        exam.AddQuestion(section.Id, held, QuestionPlacement.Unfiled, Now);
        var next = InBank();

        var added = await AddAsync(next);

        Assert.Equal([2], added.Select(q => q.Order));
        Assert.Equal([held, next], section.Questions.OrderBy(q => q.Order).Select(q => q.QuestionVersionId));
    }

    [Fact]
    public async Task AQuestionNamedTwice_CountsOnce_InThePlaceOfItsFirstMention()
    {
        var first = InBank();
        var second = InBank();

        var added = await AddAsync(first, second, first);

        Assert.Equal([first, second], added.Select(q => q.QuestionId));
    }

    [Fact]
    public async Task ARefusedQuestion_AddsNone_AndSavesNothing_EvenWhenItComesLast()
    {
        var fine = InBank();
        var alsoFine = InBank();
        var already = InBank();
        exam.AddQuestion(section.Id, already, QuestionPlacement.Unfiled, Now);

        await Assert.ThrowsAsync<DuplicateQuestionError>(() => AddAsync(fine, alsoFine, already));

        // The aggregate is whole too, not only the store: the two before the refused one were never put in.
        Assert.Equal([already], section.Questions.Select(q => q.QuestionVersionId));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AQuestionOutsideTheExamsScope_AddsNone()
    {
        var book = Guid.NewGuid();
        exam.SetScope(ExamScope.ForBook(book), new Dictionary<Guid, QuestionPlacement>(), Now);
        var inside = InBank(bookId: book, chapterId: Guid.NewGuid());
        var outside = InBank(bookId: Guid.NewGuid(), chapterId: Guid.NewGuid());

        await Assert.ThrowsAsync<QuestionOutsideExamScopeError>(() => AddAsync(inside, outside));

        Assert.Empty(section.Questions);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AQuestionTheBankWillNotLetIn_AddsNone_AndSaysWhy()
    {
        var fine = InBank();
        var retired = InBank(unusableReason: "It is retired.");

        var error = await Assert.ThrowsAsync<QuestionNotUsableError>(() => AddAsync(fine, retired));

        Assert.Contains("retired", error.Message);
        Assert.Empty(section.Questions);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AQuestionMissingFromTheBank_AddsNone_AndNamesIt()
    {
        var fine = InBank();
        var missing = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<QuestionNotInBankError>(() => AddAsync(fine, missing));

        Assert.Contains(missing.ToString(), error.Message);
        Assert.Empty(section.Questions);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePlaceOfEachQuestion_IsReadFromTheBank_SoTheScopeIsCheckedAgainstWhereItIsFiled()
    {
        var chapter = Guid.NewGuid();
        var book = Guid.NewGuid();
        exam.SetScope(ExamScope.ForBook(book), new Dictionary<Guid, QuestionPlacement>(), Now);
        var id = InBank(bookId: book, chapterId: chapter);

        var added = await AddAsync(id);

        Assert.Single(added);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AddExamQuestionsHandler.MaxCount + 1)]
    public async Task ACountOutsideTheAllowedRange_IsRefusedBeforeAnythingIsRead(int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        await Assert.ThrowsAsync<InvalidExamConfigError>(() => AddAsync(ids));

        await repository.DidNotReceive().GetByIdOrThrowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await bank.DidNotReceive().GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMissingList_IsRefusedLikeAnEmptyOne()
    {
        await Assert.ThrowsAsync<InvalidExamConfigError>(() =>
            handler.HandleAsync(new AddExamQuestionsCommand(exam.Id, section.Id, null), CancellationToken.None));

        await bank.DidNotReceive().GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheMostAllowed_AreAdded()
    {
        var ids = Enumerable.Range(0, AddExamQuestionsHandler.MaxCount).Select(_ => InBank()).ToArray();

        var added = await AddAsync(ids);

        Assert.Equal(AddExamQuestionsHandler.MaxCount, added.Count);
    }

    [Fact]
    public async Task ForAnUnknownSection_ThrowsAndAsksTheBankNothing()
    {
        var id = InBank();

        await Assert.ThrowsAsync<SectionNotFoundError>(() =>
            handler.HandleAsync(new AddExamQuestionsCommand(exam.Id, Guid.NewGuid(), [id]), CancellationToken.None));

        await bank.DidNotReceive().GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForAPublishedExam_ThrowsAndAsksTheBankNothing()
    {
        var id = InBank();
        exam.AddQuestion(section.Id, id, QuestionPlacement.Unfiled, Now);
        // An exam must be scheduled before it can be published.
        exam.Schedule(Now.AddDays(1), Now.AddDays(1).AddHours(3), null, null, 3600, Now);
        exam.Publish(Now);
        var other = InBank();

        await Assert.ThrowsAsync<ExamNotDraftError>(() => AddAsync(other));

        await bank.DidNotReceive().GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
