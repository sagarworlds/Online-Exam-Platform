using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// <summary>
/// What an exam's questions may be drawn from (FR-11): anywhere, one whole book, or chosen chapters of one book.
/// A value: changing an exam's scope replaces it. Whether the book and chapters exist is the caller's concern, because
/// they belong to the question bank; this type holds only the ids and the rule for what they allow.
/// </summary>
public sealed record ExamScope
{
    /// <summary>What kind of scope this is.</summary>
    public ExamScopeType Type { get; init; } = ExamScopeType.Independent;

    /// <summary>The book, for a <see cref="ExamScopeType.Book"/> or <see cref="ExamScopeType.Chapters"/> scope; otherwise null.</summary>
    public Guid? BookId { get; init; }

    /// <summary>The chosen chapters, for a <see cref="ExamScopeType.Chapters"/> scope; otherwise empty.</summary>
    public Guid[] ChapterIds { get; init; } = [];

    /// <summary>No limit: questions may come from anywhere in the bank.</summary>
    /// <remarks>A new instance each time: an owned value must not be shared between exams.</remarks>
    public static ExamScope Independent() => new();

    /// <summary>Limits an exam to any chapter of one book.</summary>
    /// <param name="bookId">The book.</param>
    /// <exception cref="InvalidExamConfigError">The book id is empty.</exception>
    public static ExamScope ForBook(Guid bookId)
    {
        if (bookId == Guid.Empty)
            throw new InvalidExamConfigError("Choose the book the exam covers.");

        return new ExamScope { Type = ExamScopeType.Book, BookId = bookId };
    }

    /// <summary>Limits an exam to chosen chapters of one book.</summary>
    /// <param name="bookId">The book the chapters belong to.</param>
    /// <param name="chapterIds">The chapters; repeats are ignored.</param>
    /// <exception cref="InvalidExamConfigError">The book id is empty, or no chapter, or an empty chapter id, is given.</exception>
    public static ExamScope ForChapters(Guid bookId, IEnumerable<Guid>? chapterIds)
    {
        if (bookId == Guid.Empty)
            throw new InvalidExamConfigError("Choose the book the chapters belong to.");

        var chapters = chapterIds?.Distinct().ToArray() ?? [];
        if (chapters.Length == 0)
            throw new InvalidExamConfigError("Choose at least one chapter.");
        if (chapters.Contains(Guid.Empty))
            throw new InvalidExamConfigError("A chapter id cannot be empty.");

        return new ExamScope { Type = ExamScopeType.Chapters, BookId = bookId, ChapterIds = chapters };
    }

    /// <summary>Whether a question filed here may be in an exam with this scope.</summary>
    /// <param name="placement">Where the question is filed.</param>
    public bool Allows(QuestionPlacement placement) => Type switch
    {
        ExamScopeType.Independent => true,
        ExamScopeType.Book => placement.BookId is { } book && book == BookId,
        ExamScopeType.Chapters => placement.ChapterId is { } chapter && ChapterIds.Contains(chapter),
        _ => false,
    };
}
