using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>A book under a class: the class must exist and be open when a book is put under it, and the book reports its name.</summary>
public class BookClassHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IBookRepository _books = Substitute.For<IBookRepository>();
    private readonly IQuestionRepository _questions = Substitute.For<IQuestionRepository>();
    private readonly IClassRepository _classes = Substitute.For<IClassRepository>();
    private readonly IQuestionBankUnitOfWork _unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly Clock _clock = Substitute.For<Clock>();

    public BookClassHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _questions.CountByChapterAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());
    }

    private CreateBookHandler Create => new(_books, new OpenClassResolver(_classes), _unitOfWork, _clock);

    private ChangeBookHandler Change => new(_books, _questions, _classes, new OpenClassResolver(_classes), _unitOfWork);

    private SchoolClass ClassOf(string name, bool archived = false)
    {
        var schoolClass = SchoolClass.Create(name, Author, Now);
        if (archived)
            schoolClass.Archive();

        _classes.GetByIdAsync(schoolClass.Id, Arg.Any<CancellationToken>()).Returns(schoolClass);
        _classes.GetNamesAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(schoolClass.Id)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [schoolClass.Id] = name });
        return schoolClass;
    }

    private Book BookUnder(SchoolClass? schoolClass)
    {
        var book = Book.Create("English", "English", null, Author, Now, schoolClass?.Id);
        _books.GetByIdAsync(book.Id, Arg.Any<CancellationToken>()).Returns(book);
        return book;
    }

    // ---- creating a book --------------------------------------------------------------------------

    [Fact]
    public async Task Create_UnderAnOpenClass_PutsTheBookThere_AndNamesTheClassInTheResult()
    {
        var fourth = ClassOf("4th");
        Book? stored = null;
        _books.When(b => b.Add(Arg.Any<Book>())).Do(call => stored = call.Arg<Book>());

        var dto = await Create.HandleAsync(new CreateBookCommand("English", "English", null, Author, fourth.Id), CancellationToken.None);

        Assert.Equal(fourth.Id, stored!.ClassId);
        Assert.Equal(fourth.Id, dto.ClassId);
        Assert.Equal("4th", dto.ClassName);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithoutAClass_NeedsNoLookup_AndReportsNone()
    {
        var dto = await Create.HandleAsync(new CreateBookCommand("General Knowledge", null, null, Author), CancellationToken.None);

        Assert.Null(dto.ClassId);
        Assert.Null(dto.ClassName);
        await _classes.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Create_UnderAnUnknownClass_Throws404_AndStoresNothing()
    {
        _classes.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SchoolClass?)null);

        var error = await Assert.ThrowsAsync<ClassNotFoundError>(
            () => Create.HandleAsync(new CreateBookCommand("English", null, null, Author, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
        _books.DidNotReceive().Add(Arg.Any<Book>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_UnderAnArchivedClass_Throws409_AndStoresNothing()
    {
        var old = ClassOf("3rd", archived: true);

        var error = await Assert.ThrowsAsync<ClassArchivedError>(
            () => Create.HandleAsync(new CreateBookCommand("English", null, null, Author, old.Id), CancellationToken.None));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal("class_archived", error.ErrorCode);
        _books.DidNotReceive().Add(Arg.Any<Book>());
    }

    [Fact]
    public async Task Create_WithABadBookName_Throws400_BeforeLookingTheClassUp()
    {
        await Assert.ThrowsAsync<InvalidBookError>(
            () => Create.HandleAsync(new CreateBookCommand("   ", null, null, Author, Guid.NewGuid()), CancellationToken.None));

        await _classes.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task TheSameBookNameUnderTwoClasses_IsTwoBooks()
    {
        var fourth = ClassOf("4th");
        var fifth = ClassOf("5th");
        var stored = new List<Book>();
        _books.When(b => b.Add(Arg.Any<Book>())).Do(call => stored.Add(call.Arg<Book>()));

        await Create.HandleAsync(new CreateBookCommand("English", null, null, Author, fourth.Id), CancellationToken.None);
        await Create.HandleAsync(new CreateBookCommand("English", null, null, Author, fifth.Id), CancellationToken.None);

        Assert.Equal(2, stored.Select(b => b.Id).Distinct().Count());
        Assert.Equal([fourth.Id, fifth.Id], stored.Select(b => b.ClassId));
    }

    // ---- changing a book --------------------------------------------------------------------------

    [Fact]
    public async Task Update_MovesTheBookToAnotherOpenClass()
    {
        var fourth = ClassOf("4th");
        var fifth = ClassOf("5th");
        var book = BookUnder(fourth);

        var dto = await Change.UpdateAsync(new UpdateBookCommand(book.Id, "English", "English", null, fifth.Id), CancellationToken.None);

        Assert.Equal(fifth.Id, book.ClassId);
        Assert.Equal("5th", dto.ClassName);
    }

    [Fact]
    public async Task Update_ToAnArchivedClass_Throws409_AndLeavesTheBookWhereItWas()
    {
        var fourth = ClassOf("4th");
        var old = ClassOf("3rd", archived: true);
        var book = BookUnder(fourth);

        await Assert.ThrowsAsync<ClassArchivedError>(
            () => Change.UpdateAsync(new UpdateBookCommand(book.Id, "English", "English", null, old.Id), CancellationToken.None));

        Assert.Equal(fourth.Id, book.ClassId);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ToAnUnknownClass_Throws404()
    {
        var book = BookUnder(ClassOf("4th"));
        var unknown = Guid.NewGuid();
        _classes.GetByIdAsync(unknown, Arg.Any<CancellationToken>()).Returns((SchoolClass?)null);

        await Assert.ThrowsAsync<ClassNotFoundError>(
            () => Change.UpdateAsync(new UpdateBookCommand(book.Id, "English", null, null, unknown), CancellationToken.None));
    }

    [Fact]
    public async Task Update_KeepingAClassThatHasSinceBeenArchived_IsAllowed_BecauseTheBookIsNotMoving()
    {
        var old = ClassOf("3rd", archived: true);
        var book = BookUnder(old);
        _classes.ClearReceivedCalls();

        var dto = await Change.UpdateAsync(new UpdateBookCommand(book.Id, "English, second edition", "English", null, old.Id), CancellationToken.None);

        Assert.Equal("English, second edition", dto.Name);
        Assert.Equal(old.Id, dto.ClassId);
        // Nothing asked whether the class was open: only a change of class has to name one that is.
        await _classes.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Update_WithNoClass_TakesTheBookOutOfItsClass()
    {
        var book = BookUnder(ClassOf("4th"));

        var dto = await Change.UpdateAsync(new UpdateBookCommand(book.Id, "English", "English", null), CancellationToken.None);

        Assert.Null(book.ClassId);
        Assert.Null(dto.ClassId);
        Assert.Null(dto.ClassName);
    }

    [Fact]
    public async Task Archiving_ABook_StillReportsItsClass()
    {
        var book = BookUnder(ClassOf("4th"));

        var dto = await Change.ArchiveAsync(book.Id, CancellationToken.None);

        Assert.True(dto.IsArchived);
        Assert.Equal("4th", dto.ClassName);
    }

    // ---- the resolver -----------------------------------------------------------------------------

    [Fact]
    public async Task Resolver_WithNoClassAsked_ReturnsNoName_WithoutLooking()
    {
        Assert.Null(await new OpenClassResolver(_classes).ResolveAsync(null, CancellationToken.None));

        await _classes.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Resolver_ForAnOpenClass_ReturnsItsName()
    {
        var fourth = ClassOf("4th");

        Assert.Equal("4th", await new OpenClassResolver(_classes).ResolveAsync(fourth.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_NamesTheArchivedClass_SoTheMessageSaysWhichOne()
    {
        var old = ClassOf("3rd", archived: true);

        var error = await Assert.ThrowsAsync<ClassArchivedError>(() => new OpenClassResolver(_classes).ResolveAsync(old.Id, CancellationToken.None));

        Assert.Contains("3rd", error.Message);
    }
}
