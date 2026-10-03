using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class CreateQuestionHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository books = Substitute.For<IBookRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly CreateQuestionHandler handler;

    public CreateQuestionHandlerTests()
    {
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(Now);
        // The real sanitizer, not a stub: these rules are about what survives the cleaning.
        handler = new CreateQuestionHandler(repository, new OpenChapterResolver(books), unitOfWork, new RichTextSanitizer(), clock);
    }

    private static List<NewQuestionOption> TwoOptions() => [new("Paris", true), new("Rome", false)];

    private Task<ExamPlatform.Modules.QuestionBank.Application.Dtos.QuestionDto> Create(string? text) =>
        handler.HandleAsync(new CreateQuestionCommand(text, TwoOptions(), Author), CancellationToken.None);

    [Fact]
    public async Task Create_StoresTheSanitizedHtmlNotWhatWasSent()
    {
        var created = await Create("<p>Capital of <strong>France</strong>?<script>alert(1)</script></p>");

        Assert.Equal("<p>Capital of <strong>France</strong>?</p>", created.Text);
        repository.Received(1).Add(Arg.Is<Question>(q => q.Text == created.Text));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_KeepsTheReadableTextForSearching_WithoutTheMarkup()
    {
        await Create("<p>Capital of <strong>France</strong>?</p>");

        repository.Received(1).Add(Arg.Is<Question>(q => q.SearchText == "Capital of France?"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p></p>")]
    [InlineData("<p><br></p>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Create_WithNothingVisible_IsRefusedAndNothingIsStored(string? text)
    {
        var error = await Assert.ThrowsAsync<InvalidQuestionError>(() => Create(text));

        Assert.Equal("The question text is required.", error.Message);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    private Book BookWithChapter(out Guid chapterId, bool archiveBook = false, bool archiveChapter = false)
    {
        var book = Book.Create("Maths", "Maths", null, Author, Now);
        var chapter = book.AddChapter("Algebra");
        chapterId = chapter.Id;
        if (archiveChapter) book.ArchiveChapter(chapter.Id);
        if (archiveBook) book.Archive();
        books.GetByChapterIdAsync(chapter.Id, Arg.Any<CancellationToken>()).Returns(book);
        return book;
    }

    private Task<ExamPlatform.Modules.QuestionBank.Application.Dtos.QuestionDto> CreateInChapter(Guid? chapterId) =>
        handler.HandleAsync(new CreateQuestionCommand("Capital of France?", TwoOptions(), Author, chapterId), CancellationToken.None);

    [Fact]
    public async Task Create_WithoutAChapter_IsUnfiledAndNeverLooksAtBooks()
    {
        var created = await CreateInChapter(null);

        Assert.Null(created.ChapterId);
        Assert.Null(created.BookName);
        await books.DidNotReceiveWithAnyArgs().GetByChapterIdAsync(default, default);
    }

    [Fact]
    public async Task Create_InAnOpenChapter_FilesTheQuestionAndReportsWhereItWent()
    {
        var book = BookWithChapter(out var chapterId);

        var created = await CreateInChapter(chapterId);

        Assert.Equal(chapterId, created.ChapterId);
        Assert.Equal("Algebra", created.ChapterTitle);
        Assert.Equal(book.Id, created.BookId);
        Assert.Equal("Maths", created.BookName);
        repository.Received(1).Add(Arg.Is<Question>(q => q.ChapterId == chapterId));
    }

    [Fact]
    public async Task Create_InAChapterThatDoesNotExist_Throws404AndStoresNothing()
    {
        var error = await Assert.ThrowsAsync<ChapterNotFoundError>(() => CreateInChapter(Guid.NewGuid()));

        Assert.Equal(404, error.HttpStatusCode);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Create_InAnArchivedChapterOrBook_IsRefusedAndStoresNothing(bool archiveBook, bool archiveChapter)
    {
        BookWithChapter(out var chapterId, archiveBook, archiveChapter);

        var error = await Assert.ThrowsAsync<BookArchivedError>(() => CreateInChapter(chapterId));

        Assert.Contains("archived", error.Message);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Create_WithAPictureAndNoWords_IsAccepted()
    {
        var created = await Create($"<p><img src=\"{RichTextSanitizerTests.TinyPng}\" alt=\"diagram\"></p>");

        Assert.Contains("<img", created.Text);
    }

    [Theory]
    [InlineData("<p>Look <img src=\"https://example.com/cat.png\"></p>")]
    [InlineData("<p>Look <img src=x onerror=alert(1)></p>")]
    [InlineData("<p>Look <img src=\"data:image/svg+xml;base64,PHN2Zz4=\"></p>")]
    public async Task Create_WithAPictureThatCannotBeUsed_IsRefusedWithAnActionableMessage(string text)
    {
        var error = await Assert.ThrowsAsync<InvalidQuestionError>(() => Create(text));

        Assert.Contains("image button", error.Message);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Create_WithMoreThanTheAllowedPictures_IsRefused()
    {
        var picture = $"<img src=\"{RichTextSanitizerTests.TinyPng}\">";
        var text = "<p>" + string.Concat(Enumerable.Repeat(picture, Question.MaxImages + 1)) + "</p>";

        var error = await Assert.ThrowsAsync<InvalidQuestionError>(() => Create(text));

        Assert.Contains($"at most {Question.MaxImages} images", error.Message);
    }

    [Fact]
    public async Task Create_WithTheMostPicturesAllowed_IsAccepted()
    {
        var picture = $"<img src=\"{RichTextSanitizerTests.TinyPng}\">";

        await Create("<p>" + string.Concat(Enumerable.Repeat(picture, Question.MaxImages)) + "</p>");

        repository.Received(1).Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Create_CountsReadableTextNotMarkup()
    {
        // Heavy formatting must not eat the allowance: the readable text is exactly at the limit.
        var text = "<p>" + string.Concat(Enumerable.Repeat("<strong>x</strong>", Question.MaxVisibleTextLength)) + "</p>";

        var created = await Create(text);

        Assert.True(created.Text.Length > Question.MaxVisibleTextLength);
    }

    [Fact]
    public async Task Create_WithMoreReadableTextThanAllowed_IsRefused()
    {
        var text = "<p>" + new string('x', Question.MaxVisibleTextLength + 1) + "</p>";

        var error = await Assert.ThrowsAsync<InvalidQuestionError>(() => Create(text));

        Assert.Contains($"{Question.MaxVisibleTextLength} characters", error.Message);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }
}
