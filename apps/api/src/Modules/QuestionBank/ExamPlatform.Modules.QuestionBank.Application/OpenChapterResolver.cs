using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>Finds the chapter a question is to be filed under and checks that it can take questions.</summary>
public sealed class OpenChapterResolver(IBookRepository books)
{
    /// <summary>Looks the chapter up with its book.</summary>
    /// <remarks>An archived chapter keeps the questions it has but takes nothing new, which is what archiving is for.</remarks>
    /// <param name="chapterId">The chapter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chapter with its book's id and name.</returns>
    /// <exception cref="ChapterNotFoundError">No chapter has that id.</exception>
    /// <exception cref="BookArchivedError">The chapter, or its book, is archived.</exception>
    public async Task<ChapterRef> ResolveAsync(Guid chapterId, CancellationToken cancellationToken)
    {
        var book = await books.GetByChapterIdAsync(chapterId, cancellationToken) ?? throw new ChapterNotFoundError();
        var chapter = book.GetChapter(chapterId);

        if (book.IsArchived)
            throw new BookArchivedError($"The book \"{book.Name}\" is archived; restore it or choose another chapter.");
        if (chapter.IsArchived)
            throw new BookArchivedError($"The chapter \"{chapter.Title}\" is archived; restore it or choose another chapter.");

        return new ChapterRef(chapter.Id, chapter.Title, book.Id, book.Name);
    }
}
