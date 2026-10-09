using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Turns a requested scope into an <see cref="ExamScope"/> after checking it against the question bank: the book and
/// every chosen chapter must exist, each chapter must belong to that book, and neither may be archived (an archived
/// book or chapter is kept for what is filed under it, not to be chosen for something new).
/// </summary>
public sealed class ExamScopeResolver(IBookCatalog catalog)
{
    /// <summary>Builds the scope, or says what is wrong with the request.</summary>
    /// <param name="input">The requested scope; null means no limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidExamConfigError">The scope is incomplete, or names a book or chapter that cannot be used.</exception>
    public async Task<ExamScope> ResolveAsync(ExamScopeInput? input, CancellationToken cancellationToken)
    {
        if (input is null || input.Type == ExamScopeType.Independent)
        {
            if (input is { BookId: not null } or { ChapterIds.Count: > 0 })
                throw new InvalidExamConfigError("An exam that is not limited to a book cannot name a book or chapters.");

            return ExamScope.Independent();
        }

        if (input.Type is not (ExamScopeType.Book or ExamScopeType.Chapters))
            throw new InvalidExamConfigError("The scope must be Independent, Book or Chapters.");
        if (input.BookId is not { } bookId || bookId == Guid.Empty)
            throw new InvalidExamConfigError("Choose the book the exam covers.");
        if (input.Type == ExamScopeType.Book && input.ChapterIds is { Count: > 0 })
            throw new InvalidExamConfigError("A whole-book exam does not name chapters; choose Chapters to limit it to some.");

        var book = (await catalog.GetBooksAsync([bookId], cancellationToken)).FirstOrDefault()
            ?? throw new InvalidExamConfigError("That book does not exist.");
        if (book.IsArchived)
            throw new InvalidExamConfigError($"The book \"{book.Name}\" is archived; restore it or choose another.");

        if (input.Type == ExamScopeType.Book)
            return ExamScope.ForBook(book.Id);

        // Built first so a missing chapter list gets the domain's message; then each chapter is checked against the book.
        var scope = ExamScope.ForChapters(book.Id, input.ChapterIds);
        foreach (var chapterId in scope.ChapterIds)
        {
            var chapter = book.Chapters.FirstOrDefault(c => c.Id == chapterId)
                ?? throw new InvalidExamConfigError($"A chosen chapter is not in the book \"{book.Name}\".");
            if (chapter.IsArchived)
                throw new InvalidExamConfigError($"The chapter \"{chapter.Title}\" is archived; restore it or choose another.");
        }

        return scope;
    }
}
