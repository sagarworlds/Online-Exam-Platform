using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class CreateExamHandlerTests
{
    private static IBookCatalog CatalogWith(params BookSnapshot[] books)
    {
        var catalog = Substitute.For<IBookCatalog>();
        catalog.GetBooksAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => books.Where(b => ((IReadOnlyCollection<Guid>)call[0]).Contains(b.Id)).ToList());
        return catalog;
    }

    private static CreateExamHandler HandlerFor(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, IBookCatalog? catalog = null)
    {
        catalog ??= CatalogWith();
        return new CreateExamHandler(repository, unitOfWork, new ExamScopeResolver(catalog), new ExamDtoFactory(catalog));
    }

    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesExamAndReturnsDto()
    {
        var repository = Substitute.For<IExamRepository>();
        var unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
        var handler = HandlerFor(repository, unitOfWork);

        var seriesId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var command = new CreateExamCommand(
            seriesId,
            "Mathematics Final",
            "Comprehensive mathematics assessment",
            createdBy);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Mathematics Final", result.Name);
        Assert.Equal("Comprehensive mathematics assessment", result.Description);
        Assert.Equal(seriesId, result.SeriesId);
        Assert.Equal(createdBy, result.CreatedBy);

        repository.Received(1).Add(Arg.Is<Exam>(e =>
            e.Name == "Mathematics Final" &&
            e.SeriesId == seriesId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithoutSeries_CreatesAStandaloneExam()
    {
        var repository = Substitute.For<IExamRepository>();
        var handler = HandlerFor(repository, Substitute.For<IExamAuthoringUnitOfWork>());

        var result = await handler.HandleAsync(
            new CreateExamCommand(SeriesId: null, "Standalone", null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Null(result.SeriesId);
        repository.Received(1).Add(Arg.Is<Exam>(e => e.SeriesId == null));
    }

    [Fact]
    public async Task HandleAsync_WithEmptySeriesGuid_ThrowsInvalidExamConfigErrorAndStoresNothing()
    {
        var repository = Substitute.For<IExamRepository>();
        var unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
        var handler = HandlerFor(repository, unitOfWork);

        var command = new CreateExamCommand(Guid.Empty, "Exam", null, Guid.NewGuid());

        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(
            () => handler.HandleAsync(command, CancellationToken.None));

        // A typed 400, not a 500: the empty GUID is what a form posts for a blank series field.
        Assert.Equal(400, error.HttpStatusCode);
        repository.DidNotReceive().Add(Arg.Any<Exam>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyName_ThrowsInvalidExamConfig_AndStoresNothing(string name)
    {
        // A typed 400, not an ArgumentException that reached the client as a 500.
        var repository = Substitute.For<IExamRepository>();
        var unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
        var handler = HandlerFor(repository, unitOfWork);

        var command = new CreateExamCommand(Guid.NewGuid(), name, null, Guid.NewGuid());

        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(() => handler.HandleAsync(command, CancellationToken.None));
        Assert.Equal(400, error.HttpStatusCode);
        repository.DidNotReceive().Add(Arg.Any<Exam>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithATooLongNameOrDescription_ThrowsInvalidExamConfig_InsteadOfFailingInTheDatabase()
    {
        var handler = HandlerFor(Substitute.For<IExamRepository>(), Substitute.For<IExamAuthoringUnitOfWork>());

        await Assert.ThrowsAsync<InvalidExamConfigError>(() =>
            handler.HandleAsync(new CreateExamCommand(null, new string('x', Exam.MaxNameLength + 1), null, Guid.NewGuid()), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidExamConfigError>(() =>
            handler.HandleAsync(new CreateExamCommand(null, "Maths", new string('x', Exam.MaxDescriptionLength + 1), Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_TrimsTheNameAndTurnsABlankDescriptionIntoNone()
    {
        var handler = HandlerFor(Substitute.For<IExamRepository>(), Substitute.For<IExamAuthoringUnitOfWork>());

        var dto = await handler.HandleAsync(new CreateExamCommand(null, "  Maths mock  ", "   ", Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("Maths mock", dto.Name);
        Assert.Null(dto.Description);
    }

    [Fact]
    public async Task HandleAsync_WithoutAScope_CreatesAnIndependentExam()
    {
        var repository = Substitute.For<IExamRepository>();
        var handler = HandlerFor(repository, Substitute.For<IExamAuthoringUnitOfWork>());

        var result = await handler.HandleAsync(new CreateExamCommand(null, "Free", null, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ExamScopeType.Independent, result.Scope!.Type);
        repository.Received(1).Add(Arg.Is<Exam>(e => e.Scope.Type == ExamScopeType.Independent));
    }

    [Fact]
    public async Task HandleAsync_WithAChapterScope_StoresItAndReportsTheNames()
    {
        var chapter = new ChapterSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Algebra", 1, false);
        var book = new BookSnapshot(chapter.BookId, "Maths", false, [chapter]);
        var repository = Substitute.For<IExamRepository>();
        var handler = HandlerFor(repository, Substitute.For<IExamAuthoringUnitOfWork>(), CatalogWith(book));

        var result = await handler.HandleAsync(
            new CreateExamCommand(null, "Algebra test", null, Guid.NewGuid(), new ExamScopeInput(ExamScopeType.Chapters, book.Id, [chapter.Id])),
            CancellationToken.None);

        Assert.Equal(ExamScopeType.Chapters, result.Scope!.Type);
        Assert.Equal("Maths", result.Scope.BookName);
        Assert.Equal([new ExamScopeChapterDto(chapter.Id, "Algebra")], result.Scope.Chapters);
        repository.Received(1).Add(Arg.Is<Exam>(e => e.Scope.Type == ExamScopeType.Chapters && e.Scope.ChapterIds.SequenceEqual(new[] { chapter.Id })));
    }

    [Fact]
    public async Task HandleAsync_WithABookThatDoesNotExist_Throws400AndStoresNothing()
    {
        var repository = Substitute.For<IExamRepository>();
        var unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
        var handler = HandlerFor(repository, unitOfWork);

        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(() => handler.HandleAsync(
            new CreateExamCommand(null, "Exam", null, Guid.NewGuid(), new ExamScopeInput(ExamScopeType.Book, Guid.NewGuid())), CancellationToken.None));

        Assert.Equal(400, error.HttpStatusCode);
        repository.DidNotReceive().Add(Arg.Any<Exam>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
