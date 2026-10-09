using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>
/// A chapter of a <see cref="Book"/>: the unit questions are filed under and exams can be limited to. A chapter is
/// only ever created, renamed, archived or restored through its book, which owns the rules between chapters.
/// </summary>
public sealed class Chapter : Entity
{
    /// <summary>The book this chapter belongs to.</summary>
    public Guid BookId { get; private set; }

    /// <summary>The chapter's title, unique within its book.</summary>
    public string Title { get; private set; }

    /// <summary>Position within the book, from 1, in the order the chapters were added.</summary>
    public int Order { get; private set; }

    /// <summary>
    /// Whether the chapter is archived. An archived chapter keeps its questions but takes no new ones and is hidden
    /// from pickers; chapters are archived rather than deleted so nothing filed under them is ever lost.
    /// </summary>
    public bool IsArchived { get; private set; }

    // For EF Core.
    private Chapter() : base(Guid.Empty) => Title = null!;

    internal Chapter(Guid bookId, string title, int order) : base(Guid.NewGuid())
    {
        BookId = bookId;
        Title = title;
        Order = order;
    }

    internal void Rename(string title) => Title = title;

    internal void SetArchived(bool archived) => IsArchived = archived;
}
