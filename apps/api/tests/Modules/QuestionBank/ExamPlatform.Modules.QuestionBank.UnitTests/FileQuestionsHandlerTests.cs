using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Filing questions under a chapter (FR-5): one at a time or in bulk, all of them or none.</summary>
public class FileQuestionsHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository books = Substitute.For<IBookRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly IQuestionPlacementGuard guard = Substitute.For<IQuestionPlacementGuard>();
    private readonly FileQuestionsHandler handler;
    private readonly Book book;
    private readonly Guid chapterId;

    public FileQuestionsHandlerTests()
    {
        handler = new FileQuestionsHandler(repository, new OpenChapterResolver(books), [guard], unitOfWork);
        book = Book.Create("Maths", "Maths", null, Guid.NewGuid(), Now);
        book.AddChapter("Algebra");
        chapterId = book.Chapters.Single().Id;
        books.GetByChapterIdAsync(chapterId, Arg.Any<CancellationToken>()).Returns(book);
        guard.CheckAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private List<Question> Stored(int count, Guid? filedUnder = null)
    {
        var questions = Enumerable.Range(1, count)
            .Select(i => Question.Create($"Q{i}", [new("A", true), new("B", false)], Guid.NewGuid(), Now, filedUnder))
            .ToList();
        repository.GetManyForUpdateAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => questions.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList());
        return questions;
    }

    private Task<FileQuestionsResult> File(IEnumerable<Question> questions) =>
        handler.HandleAsync(new FileQuestionsCommand(questions.Select(q => q.Id).ToList(), chapterId), CancellationToken.None);

    [Fact]
    public async Task EveryQuestionChosen_IsFiledUnderTheChapter_InOneSave()
    {
        var questions = Stored(3);

        var result = await File(questions);

        Assert.Equal(3, result.Moved);
        Assert.All(questions, q => Assert.Equal(chapterId, q.ChapterId));
        Assert.Equal(("Algebra", "Maths", book.Id), (result.ChapterTitle, result.BookName, result.BookId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneQuestion_IsJustABulkOfOne()
    {
        var questions = Stored(1);

        Assert.Equal(1, (await File(questions)).Moved);
    }

    [Fact]
    public async Task AQuestionAlreadyInTheChapter_IsNotCountedAsMoved()
    {
        var questions = Stored(2, filedUnder: chapterId);

        var result = await File(questions);

        Assert.Equal(0, result.Moved);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AQuestionFiledElsewhere_IsMoved()
    {
        var questions = Stored(1, filedUnder: Guid.NewGuid());

        await File(questions);

        Assert.Equal(chapterId, questions[0].ChapterId);
    }

    [Fact]
    public async Task TheSameQuestionTwice_IsFiledOnce()
    {
        var questions = Stored(1);

        var result = await handler.HandleAsync(new FileQuestionsCommand([questions[0].Id, questions[0].Id], chapterId), CancellationToken.None);

        Assert.Equal(1, result.Moved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(FileQuestionsHandler.MaxQuestions + 1)]
    public async Task NoQuestions_OrTooMany_IsRefused(int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

        await Assert.ThrowsAsync<InvalidQuestionError>(() => handler.HandleAsync(new FileQuestionsCommand(ids, chapterId), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidQuestionError>(() => handler.HandleAsync(new FileQuestionsCommand(null, chapterId), CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownChapter_IsNotFound_AndNothingMoves()
    {
        var questions = Stored(1);

        await Assert.ThrowsAsync<ChapterNotFoundError>(() => handler.HandleAsync(new FileQuestionsCommand([questions[0].Id], Guid.NewGuid()), CancellationToken.None));

        Assert.Null(questions[0].ChapterId);
    }

    [Fact]
    public async Task AnArchivedChapterOrBook_TakesNothingNew()
    {
        var questions = Stored(1);
        book.ArchiveChapter(chapterId);

        await Assert.ThrowsAsync<BookArchivedError>(() => File(questions));

        book.RestoreChapter(chapterId);
        book.Archive();
        await Assert.ThrowsAsync<BookArchivedError>(() => File(questions));
        Assert.Null(questions[0].ChapterId);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneUnknownQuestion_RefusesTheWholeRequest_AndNothingMoves()
    {
        var questions = Stored(2);

        await Assert.ThrowsAsync<QuestionNotFoundError>(() =>
            handler.HandleAsync(new FileQuestionsCommand([questions[0].Id, questions[1].Id, Guid.NewGuid()], chapterId), CancellationToken.None));

        Assert.All(questions, q => Assert.Null(q.ChapterId));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnObjection_RefusesTheWholeRequest_NamesTheReason_AndNothingMoves()
    {
        var questions = Stored(3);
        guard.CheckAsync(Arg.Any<IReadOnlyCollection<Guid>>(), book.Id, chapterId, Arg.Any<CancellationToken>()).Returns(
        [
            new PlacementObjection(questions[1].Id, "The draft exam \"Maths mock\" only takes questions from other chapters."),
            new PlacementObjection(questions[2].Id, "The draft exam \"Maths mock\" only takes questions from other chapters."),
        ]);

        var error = await Assert.ThrowsAsync<PlacementRefusedError>(() => File(questions));

        Assert.Equal(
            "The draft exam \"Maths mock\" only takes questions from other chapters. 1 other question is held back for the same reason. Nothing was moved.",
            error.Message);
        Assert.Equal("placement_refused", error.ErrorCode);
        Assert.All(questions, q => Assert.Null(q.ChapterId));
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryGuardIsAsked_NotJustTheFirst()
    {
        var second = Substitute.For<IQuestionPlacementGuard>();
        var questions = Stored(1);
        second.CheckAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([new PlacementObjection(questions[0].Id, "Another module objects.")]);
        var both = new FileQuestionsHandler(repository, new OpenChapterResolver(books), [guard, second], unitOfWork);

        var error = await Assert.ThrowsAsync<PlacementRefusedError>(() => both.HandleAsync(new FileQuestionsCommand([questions[0].Id], chapterId), CancellationToken.None));

        Assert.StartsWith("Another module objects.", error.Message);
    }
}

/// <summary>The aggregate's own part of filing.</summary>
public class QuestionFileUnderTests
{
    private static Question New(Guid? chapter = null) =>
        Question.Create("Q?", [new("A", true), new("B", false)], Guid.NewGuid(), new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc), chapter);

    [Fact]
    public void FilingAnUnfiledQuestion_MovesIt()
    {
        var question = New();
        var chapter = Guid.NewGuid();

        Assert.True(question.FileUnder(chapter));
        Assert.Equal(chapter, question.ChapterId);
    }

    [Fact]
    public void FilingWhereItAlreadyIs_ChangesNothing()
    {
        var chapter = Guid.NewGuid();
        var question = New(chapter);

        Assert.False(question.FileUnder(chapter));
        Assert.Equal(chapter, question.ChapterId);
    }

    [Fact]
    public void FilingLeavesTheContentAlone()
    {
        var question = New();
        var options = question.Options.Select(o => (o.Id, o.Text, o.IsCorrect)).ToList();

        question.FileUnder(Guid.NewGuid());

        Assert.Equal("Q?", question.Text);
        Assert.Equal(options, question.Options.Select(o => (o.Id, o.Text, o.IsCorrect)));
    }
}
