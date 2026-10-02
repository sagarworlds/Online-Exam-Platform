using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class SetExamScopeHandlerTests
{
    private readonly IExamRepository repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly IQuestionBank questionBank = Substitute.For<IQuestionBank>();
    private readonly IBookCatalog catalog = Substitute.For<IBookCatalog>();
    private readonly SetExamScopeHandler handler;

    private readonly Guid bookId = Guid.NewGuid();
    private readonly ChapterSnapshot algebra;
    private readonly ChapterSnapshot geometry;
    private readonly Exam exam = new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    public SetExamScopeHandlerTests()
    {
        algebra = new ChapterSnapshot(Guid.NewGuid(), bookId, "Algebra", 1, false);
        geometry = new ChapterSnapshot(Guid.NewGuid(), bookId, "Geometry", 2, false);
        var book = new BookSnapshot(bookId, "Maths Grade 10", false, [algebra, geometry]);
        catalog.GetBooksAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
            ((IReadOnlyCollection<Guid>)call[0]).Contains(bookId) ? new List<BookSnapshot> { book } : []);

        repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        handler = new SetExamScopeHandler(repository, unitOfWork, questionBank, new ExamScopeResolver(catalog), new ExamDtoFactory(catalog));
    }

    private QuestionSnapshot Question(Guid id, ChapterSnapshot? chapter) =>
        new(id, "Q", [], chapter?.Id, chapter?.BookId);

    private void PutInExam(params QuestionSnapshot[] questions)
    {
        var section = exam.AddSection("S", null);
        foreach (var q in questions)
            exam.AddQuestion(section.Id, q.Id, new QuestionPlacement(q.BookId, q.ChapterId));
        questionBank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(questions);
    }

    [Fact]
    public async Task SettingAScope_OnAnEmptyExam_SavesItAndReportsTheNames()
    {
        var result = await handler.HandleAsync(
            new SetExamScopeCommand(exam.Id, new ExamScopeInput(ExamScopeType.Chapters, bookId, [algebra.Id])), CancellationToken.None);

        Assert.Equal(ExamScopeType.Chapters, exam.Scope.Type);
        Assert.Equal("Maths Grade 10", result.Scope!.BookName);
        Assert.Equal("Algebra", Assert.Single(result.Scope.Chapters).Title);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewScope_ThatHoldsEveryQuestionAlreadyInTheExam_IsAccepted()
    {
        PutInExam(Question(Guid.NewGuid(), algebra), Question(Guid.NewGuid(), geometry));

        await handler.HandleAsync(new SetExamScopeCommand(exam.Id, new ExamScopeInput(ExamScopeType.Book, bookId)), CancellationToken.None);

        Assert.Equal(ExamScopeType.Book, exam.Scope.Type);
    }

    [Fact]
    public async Task ANewScope_ThatWouldLeaveAQuestionOutside_IsRefused_NamesIt_AndSavesNothing()
    {
        var keep = Question(Guid.NewGuid(), algebra);
        var leave = Question(Guid.NewGuid(), geometry);
        PutInExam(keep, leave);

        var error = await Assert.ThrowsAsync<QuestionOutsideExamScopeError>(() => handler.HandleAsync(
            new SetExamScopeCommand(exam.Id, new ExamScopeInput(ExamScopeType.Chapters, bookId, [algebra.Id])), CancellationToken.None));

        Assert.Equal([leave.Id], error.QuestionIds);
        Assert.Equal(ExamScopeType.Independent, exam.Scope.Type);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LiftingTheLimit_AlwaysWorks()
    {
        PutInExam(Question(Guid.NewGuid(), null));
        exam.SetScope(ExamScope.Independent(), new Dictionary<Guid, QuestionPlacement>());

        await handler.HandleAsync(new SetExamScopeCommand(exam.Id, new ExamScopeInput(ExamScopeType.Independent)), CancellationToken.None);

        Assert.Equal(ExamScopeType.Independent, exam.Scope.Type);
    }

    [Fact]
    public async Task ABadScope_IsRefusedBeforeTheQuestionsAreEvenRead()
    {
        await Assert.ThrowsAsync<InvalidExamConfigError>(() => handler.HandleAsync(
            new SetExamScopeCommand(exam.Id, new ExamScopeInput(ExamScopeType.Book, Guid.NewGuid())), CancellationToken.None));

        await questionBank.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
