using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>
/// A book: the unit authors organise questions by, made of chapters (FR-5). Exams can later be limited to a book or
/// to some of its chapters. The book owns its chapters, so the rules between them (unique titles, nothing added to
/// an archived book) live here and no caller can break them.
/// </summary>
public sealed class Book : AggregateRoot
{
    /// <summary>The longest book name, after trimming.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The longest subject, after trimming.</summary>
    public const int MaxSubjectLength = 100;

    /// <summary>The longest description, after trimming.</summary>
    public const int MaxDescriptionLength = 1000;

    /// <summary>The longest chapter title, after trimming.</summary>
    public const int MaxChapterTitleLength = 200;

    private readonly List<Chapter> _chapters = [];

    /// <summary>The book's name.</summary>
    public string Name { get; private set; }

    /// <summary>The subject the book covers (for example "Maths"), or null when it is not given.</summary>
    public string? Subject { get; private set; }

    /// <summary>A short description, or null.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Whether the book is archived: kept for the questions filed under it, but closed to new chapters and hidden from
    /// pickers. Books are archived rather than deleted so nothing filed under them is ever lost.
    /// </summary>
    public bool IsArchived { get; private set; }

    /// <summary>The authoring user who created the book.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>When the book was created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>The chapters, in the order they were added.</summary>
    public IReadOnlyList<Chapter> Chapters => _chapters.OrderBy(c => c.Order).ToList();

    // For EF Core.
    private Book() : base(Guid.Empty) => Name = null!;

    private Book(Guid id, string name, string? subject, string? description, Guid createdBy, DateTime createdAtUtc) : base(id)
    {
        Name = name;
        Subject = subject;
        Description = description;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Creates a book after checking its details.</summary>
    /// <param name="name">The book's name; leading and trailing whitespace is removed.</param>
    /// <param name="subject">The subject, or null or blank for none.</param>
    /// <param name="description">A description, or null or blank for none.</param>
    /// <param name="createdBy">The authoring user.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidBookError">The name is blank, or a field is too long.</exception>
    public static Book Create(string? name, string? subject, string? description, Guid createdBy, DateTime nowUtc)
    {
        var details = Validate(name, subject, description);
        return new Book(Guid.NewGuid(), details.Name, details.Subject, details.Description, createdBy, nowUtc);
    }

    /// <summary>Changes the book's name, subject and description.</summary>
    /// <param name="name">The new name.</param>
    /// <param name="subject">The new subject, or null or blank for none.</param>
    /// <param name="description">The new description, or null or blank for none.</param>
    /// <exception cref="InvalidBookError">The name is blank, or a field is too long.</exception>
    public void Update(string? name, string? subject, string? description)
    {
        var details = Validate(name, subject, description);
        Name = details.Name;
        Subject = details.Subject;
        Description = details.Description;
    }

    /// <summary>Archives the book: it keeps its questions, takes no new chapters and leaves the pickers.</summary>
    public void Archive() => IsArchived = true;

    /// <summary>Brings an archived book back into use.</summary>
    public void Restore() => IsArchived = false;

    /// <summary>Adds a chapter after the existing ones.</summary>
    /// <param name="title">The chapter's title; leading and trailing whitespace is removed.</param>
    /// <returns>The new chapter.</returns>
    /// <exception cref="BookArchivedError">The book is archived.</exception>
    /// <exception cref="InvalidBookError">The title is blank or too long.</exception>
    /// <exception cref="DuplicateChapterError">The book already has a chapter with that title.</exception>
    public Chapter AddChapter(string? title)
    {
        if (IsArchived)
            throw new BookArchivedError("This book is archived; restore it before adding chapters.");

        var trimmed = ValidateChapterTitle(title);
        EnsureTitleIsFree(trimmed, exceptChapterId: null);

        var chapter = new Chapter(Id, trimmed, _chapters.Count == 0 ? 1 : _chapters.Max(c => c.Order) + 1);
        _chapters.Add(chapter);
        return chapter;
    }

    /// <summary>Renames a chapter.</summary>
    /// <param name="chapterId">The chapter to rename.</param>
    /// <param name="title">The new title.</param>
    /// <exception cref="ChapterNotFoundError">The book has no such chapter.</exception>
    /// <exception cref="InvalidBookError">The title is blank or too long.</exception>
    /// <exception cref="DuplicateChapterError">Another chapter of the book already has that title.</exception>
    public void RenameChapter(Guid chapterId, string? title)
    {
        var chapter = GetChapter(chapterId);
        var trimmed = ValidateChapterTitle(title);
        EnsureTitleIsFree(trimmed, exceptChapterId: chapter.Id);
        chapter.Rename(trimmed);
    }

    /// <summary>Archives a chapter: it keeps its questions, takes no new ones and leaves the pickers.</summary>
    /// <param name="chapterId">The chapter to archive.</param>
    /// <exception cref="ChapterNotFoundError">The book has no such chapter.</exception>
    public void ArchiveChapter(Guid chapterId) => GetChapter(chapterId).SetArchived(true);

    /// <summary>Brings an archived chapter back into use.</summary>
    /// <param name="chapterId">The chapter to restore.</param>
    /// <exception cref="ChapterNotFoundError">The book has no such chapter.</exception>
    public void RestoreChapter(Guid chapterId) => GetChapter(chapterId).SetArchived(false);

    /// <summary>Finds one of this book's chapters.</summary>
    /// <param name="chapterId">The chapter's id.</param>
    /// <exception cref="ChapterNotFoundError">The book has no such chapter.</exception>
    public Chapter GetChapter(Guid chapterId) =>
        _chapters.FirstOrDefault(c => c.Id == chapterId) ?? throw new ChapterNotFoundError();

    private void EnsureTitleIsFree(string title, Guid? exceptChapterId)
    {
        // Case-insensitive, and counting archived chapters too, so restoring one can never create a clash.
        if (_chapters.Any(c => c.Id != exceptChapterId && string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateChapterError(title);
    }

    private static string ValidateChapterTitle(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidBookError("A chapter needs a title.");
        if (trimmed.Length > MaxChapterTitleLength)
            throw new InvalidBookError($"A chapter title must be at most {MaxChapterTitleLength} characters.");
        return trimmed;
    }

    private static (string Name, string? Subject, string? Description) Validate(string? name, string? subject, string? description)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            throw new InvalidBookError("A book needs a name.");
        if (trimmedName.Length > MaxNameLength)
            throw new InvalidBookError($"A book name must be at most {MaxNameLength} characters.");

        var trimmedSubject = NullIfBlank(subject);
        if (trimmedSubject is { Length: > MaxSubjectLength })
            throw new InvalidBookError($"A subject must be at most {MaxSubjectLength} characters.");

        var trimmedDescription = NullIfBlank(description);
        if (trimmedDescription is { Length: > MaxDescriptionLength })
            throw new InvalidBookError($"A description must be at most {MaxDescriptionLength} characters.");

        return (trimmedName, trimmedSubject, trimmedDescription);
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
