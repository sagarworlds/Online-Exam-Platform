using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class BookTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private static Book NewBook() => Book.Create("Maths Grade 10", "Maths", null, Author, Now);

    [Fact]
    public void Create_StoresTrimmedDetails()
    {
        var book = Book.Create("  Maths Grade 10  ", "  Maths ", "  Algebra and geometry  ", Author, Now);

        Assert.Equal("Maths Grade 10", book.Name);
        Assert.Equal("Maths", book.Subject);
        Assert.Equal("Algebra and geometry", book.Description);
        Assert.Equal(Author, book.CreatedBy);
        Assert.Equal(Now, book.CreatedAtUtc);
        Assert.False(book.IsArchived);
        Assert.Empty(book.Chapters);
        Assert.NotEqual(Guid.Empty, book.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutAName_Throws(string? name)
    {
        var error = Assert.Throws<InvalidBookError>(() => Book.Create(name, null, null, Author, Now));
        Assert.Equal("invalid_book", error.ErrorCode);
        Assert.Equal(400, error.HttpStatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_TreatsABlankSubjectAndDescriptionAsNone(string? blank)
    {
        var book = Book.Create("Physics", blank, blank, Author, Now);

        Assert.Null(book.Subject);
        Assert.Null(book.Description);
    }

    [Fact]
    public void Create_WithTooLongDetails_Throws()
    {
        Assert.Throws<InvalidBookError>(() => Book.Create(new string('x', Book.MaxNameLength + 1), null, null, Author, Now));
        Assert.Throws<InvalidBookError>(() => Book.Create("B", new string('x', Book.MaxSubjectLength + 1), null, Author, Now));
        Assert.Throws<InvalidBookError>(() => Book.Create("B", null, new string('x', Book.MaxDescriptionLength + 1), Author, Now));
    }

    [Fact]
    public void Update_ChangesTheDetails_AndRefusesABlankName()
    {
        var book = NewBook();

        book.Update(" Maths Grade 11 ", null, "New edition");

        Assert.Equal("Maths Grade 11", book.Name);
        Assert.Null(book.Subject);
        Assert.Equal("New edition", book.Description);
        Assert.Throws<InvalidBookError>(() => book.Update("  ", "Maths", null));
        Assert.Equal("Maths Grade 11", book.Name);
    }

    [Fact]
    public void AddChapter_AppendsInOrderStartingAtOne()
    {
        var book = NewBook();

        var first = book.AddChapter("  Algebra ");
        var second = book.AddChapter("Geometry");

        Assert.Equal("Algebra", first.Title);
        Assert.Equal([1, 2], book.Chapters.Select(c => c.Order));
        Assert.Equal(["Algebra", "Geometry"], book.Chapters.Select(c => c.Title));
        Assert.All(book.Chapters, c => Assert.Equal(book.Id, c.BookId));
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void AddChapter_WithoutATitle_Throws(string? title) =>
        Assert.Throws<InvalidBookError>(() => NewBook().AddChapter(title));

    [Fact]
    public void AddChapter_WithATooLongTitle_Throws() =>
        Assert.Throws<InvalidBookError>(() => NewBook().AddChapter(new string('x', Book.MaxChapterTitleLength + 1)));

    [Theory]
    [InlineData("Algebra")]
    [InlineData("algebra")]
    [InlineData("  ALGEBRA  ")]
    public void AddChapter_WithATitleTheBookAlreadyHas_Throws(string title)
    {
        var book = NewBook();
        book.AddChapter("Algebra");

        var error = Assert.Throws<DuplicateChapterError>(() => book.AddChapter(title));

        Assert.Equal("duplicate_chapter", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Single(book.Chapters);
    }

    [Fact]
    public void AddChapter_ToAnArchivedBook_Throws_UntilItIsRestored()
    {
        var book = NewBook();
        book.Archive();

        var error = Assert.Throws<BookArchivedError>(() => book.AddChapter("Algebra"));
        Assert.Equal("book_archived", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);

        book.Restore();
        Assert.Equal("Algebra", book.AddChapter("Algebra").Title);
    }

    [Fact]
    public void RenameChapter_ChangesTheTitle_AndMayOnlyChangeItsCase()
    {
        var book = NewBook();
        var chapter = book.AddChapter("algebra");

        book.RenameChapter(chapter.Id, "Algebra");

        Assert.Equal("Algebra", book.GetChapter(chapter.Id).Title);
    }

    [Fact]
    public void RenameChapter_ToAnotherChaptersTitle_Throws()
    {
        var book = NewBook();
        book.AddChapter("Algebra");
        var geometry = book.AddChapter("Geometry");

        Assert.Throws<DuplicateChapterError>(() => book.RenameChapter(geometry.Id, "algebra"));
        Assert.Equal("Geometry", book.GetChapter(geometry.Id).Title);
    }

    [Fact]
    public void RenameChapter_OfAnUnknownChapter_Throws()
    {
        var error = Assert.Throws<ChapterNotFoundError>(() => NewBook().RenameChapter(Guid.NewGuid(), "x"));
        Assert.Equal(404, error.HttpStatusCode);
    }

    [Fact]
    public void ArchivingAChapter_KeepsItsTitleReserved_SoRestoringItCannotClash()
    {
        var book = NewBook();
        var chapter = book.AddChapter("Algebra");
        book.ArchiveChapter(chapter.Id);

        Assert.True(book.GetChapter(chapter.Id).IsArchived);
        Assert.Throws<DuplicateChapterError>(() => book.AddChapter("Algebra"));

        book.RestoreChapter(chapter.Id);
        Assert.False(book.GetChapter(chapter.Id).IsArchived);
    }

    [Fact]
    public void ANewChapterAfterAnArchivedOne_StillGetsTheNextPosition()
    {
        var book = NewBook();
        book.ArchiveChapter(book.AddChapter("One").Id);

        var two = book.AddChapter("Two");

        Assert.Equal(2, two.Order);
    }

    [Fact]
    public void ArchivingABook_DoesNotArchiveItsChapters()
    {
        var book = NewBook();
        book.AddChapter("Algebra");

        book.Archive();

        Assert.True(book.IsArchived);
        Assert.All(book.Chapters, c => Assert.False(c.IsArchived));
    }
}
