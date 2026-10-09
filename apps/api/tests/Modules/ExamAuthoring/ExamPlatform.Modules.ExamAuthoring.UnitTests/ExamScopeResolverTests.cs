using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class ExamScopeResolverTests
{
    private readonly BookSnapshot maths;
    private readonly ChapterSnapshot algebra;
    private readonly ChapterSnapshot oldChapter;
    private readonly ExamScopeResolver resolver;

    public ExamScopeResolverTests()
    {
        var bookId = Guid.NewGuid();
        algebra = new ChapterSnapshot(Guid.NewGuid(), bookId, "Algebra", 1, false);
        oldChapter = new ChapterSnapshot(Guid.NewGuid(), bookId, "Old chapter", 2, true);
        maths = new BookSnapshot(bookId, "Maths", false, [algebra, oldChapter]);
        var archived = new BookSnapshot(Archived, "Old Physics", true, []);

        var catalog = Substitute.For<IBookCatalog>();
        catalog.GetBooksAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => new[] { maths, archived }.Where(b => ((IReadOnlyCollection<Guid>)call[0]).Contains(b.Id)).ToList());
        resolver = new ExamScopeResolver(catalog);
    }

    private static readonly Guid Archived = Guid.NewGuid();

    private Task<ExamScope> Resolve(ExamScopeInput? input) => resolver.ResolveAsync(input, CancellationToken.None);

    private async Task AssertRefused(ExamScopeInput? input, string messagePart)
    {
        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(() => Resolve(input));
        Assert.Equal(400, error.HttpStatusCode);
        Assert.Contains(messagePart, error.Message);
    }

    [Fact]
    public async Task NothingOrIndependent_MeansNoLimit()
    {
        Assert.Equal(ExamScopeType.Independent, (await Resolve(null)).Type);
        Assert.Equal(ExamScopeType.Independent, (await Resolve(new ExamScopeInput(ExamScopeType.Independent))).Type);
    }

    [Fact]
    public async Task Independent_MayNotAlsoNameABookOrChapters()
    {
        await AssertRefused(new ExamScopeInput(ExamScopeType.Independent, maths.Id), "not limited to a book");
        await AssertRefused(new ExamScopeInput(ExamScopeType.Independent, null, [algebra.Id]), "not limited to a book");
    }

    [Fact]
    public async Task ABookScope_OfAnExistingOpenBook_Resolves()
    {
        var scope = await Resolve(new ExamScopeInput(ExamScopeType.Book, maths.Id));

        Assert.Equal(ExamScopeType.Book, scope.Type);
        Assert.Equal(maths.Id, scope.BookId);
    }

    [Fact]
    public async Task AChaptersScope_OfOpenChaptersOfTheBook_Resolves()
    {
        var scope = await Resolve(new ExamScopeInput(ExamScopeType.Chapters, maths.Id, [algebra.Id]));

        Assert.Equal([algebra.Id], scope.ChapterIds);
    }

    [Fact]
    public async Task ABookOrChaptersScope_WithoutABook_IsRefused()
    {
        await AssertRefused(new ExamScopeInput(ExamScopeType.Book), "Choose the book");
        await AssertRefused(new ExamScopeInput(ExamScopeType.Chapters, Guid.Empty, [algebra.Id]), "Choose the book");
    }

    [Fact]
    public async Task ABookThatDoesNotExist_IsRefused() =>
        await AssertRefused(new ExamScopeInput(ExamScopeType.Book, Guid.NewGuid()), "does not exist");

    [Fact]
    public async Task AnArchivedBook_IsRefused_BecauseItIsKeptForWhatIsFiledNotForNewExams() =>
        await AssertRefused(new ExamScopeInput(ExamScopeType.Book, Archived), "archived");

    [Fact]
    public async Task AWholeBookScope_MayNotNameChapters() =>
        await AssertRefused(new ExamScopeInput(ExamScopeType.Book, maths.Id, [algebra.Id]), "does not name chapters");

    [Fact]
    public async Task AChaptersScope_NeedsAtLeastOneChapter() =>
        await AssertRefused(new ExamScopeInput(ExamScopeType.Chapters, maths.Id, []), "at least one chapter");

    [Fact]
    public async Task AChapterThatIsNotInTheBook_IsRefused() =>
        await AssertRefused(new ExamScopeInput(ExamScopeType.Chapters, maths.Id, [algebra.Id, Guid.NewGuid()]), "is not in the book");

    [Fact]
    public async Task AnArchivedChapter_IsRefused() =>
        await AssertRefused(new ExamScopeInput(ExamScopeType.Chapters, maths.Id, [oldChapter.Id]), "is archived");

    [Fact]
    public async Task AnUnknownScopeType_IsRefused() =>
        await AssertRefused(new ExamScopeInput((ExamScopeType)99, maths.Id), "Independent, Book or Chapters");
}
