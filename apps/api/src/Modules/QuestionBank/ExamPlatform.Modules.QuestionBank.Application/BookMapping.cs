using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>Maps <see cref="Book"/> to its DTO, so every handler reports a book the same way.</summary>
internal static class BookMapping
{
    /// <summary>Maps a book and its chapters, in the order they were added.</summary>
    /// <param name="book">The book to map.</param>
    /// <param name="questionCounts">How many questions are filed under each chapter; a missing chapter has none.</param>
    /// <param name="className">The name of the book's class, when it has one; the book carries only the class's id.</param>
    public static BookDto ToDto(this Book book, IReadOnlyDictionary<Guid, int> questionCounts, string? className = null) =>
        new(
            book.Id,
            book.Name,
            book.Subject,
            book.Description,
            book.IsArchived,
            book.Chapters.Select(c => new ChapterDto(c.Id, c.BookId, c.Title, c.Order, c.IsArchived, questionCounts.GetValueOrDefault(c.Id))).ToList(),
            book.CreatedBy,
            book.CreatedAtUtc,
            book.ClassId,
            className);
}
