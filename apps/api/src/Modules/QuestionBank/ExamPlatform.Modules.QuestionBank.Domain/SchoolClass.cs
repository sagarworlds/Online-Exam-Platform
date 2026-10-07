using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain;

/// <summary>
/// A class (a standard or grade, such as "4th"): the level above books. A book may belong to one class, so "English" for the 4th
/// and "English" for the 5th are two books, each with its own chapters and questions, and an exam limited to a book or to some of
/// its chapters is, in effect, an exam for that class. Classes are archived rather than deleted, so nothing filed under their
/// books is ever lost.
/// </summary>
public sealed class SchoolClass : AggregateRoot
{
    /// <summary>The longest class name, after trimming.</summary>
    public const int MaxNameLength = 100;

    /// <summary>The class's name, for example "4th" or "Class 4".</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Whether the class is archived: kept for the books filed under it, but closed to new books and hidden from pickers.
    /// </summary>
    public bool IsArchived { get; private set; }

    /// <summary>The authoring user who created the class.</summary>
    public Guid CreatedBy { get; private set; }

    /// <summary>When the class was created.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    // For EF Core.
    private SchoolClass() : base(Guid.Empty) => Name = null!;

    private SchoolClass(Guid id, string name, Guid createdBy, DateTime createdAtUtc) : base(id)
    {
        Name = name;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Creates a class after checking its name.</summary>
    /// <param name="name">The class's name; leading and trailing whitespace is removed.</param>
    /// <param name="createdBy">The authoring user.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <exception cref="InvalidClassError">The name is blank or too long.</exception>
    public static SchoolClass Create(string? name, Guid createdBy, DateTime nowUtc) =>
        new(Guid.NewGuid(), ValidateName(name), createdBy, nowUtc);

    /// <summary>Renames the class.</summary>
    /// <param name="name">The new name.</param>
    /// <exception cref="InvalidClassError">The name is blank or too long.</exception>
    public void Rename(string? name) => Name = ValidateName(name);

    /// <summary>Archives the class: it keeps its books, takes no new ones and leaves the pickers.</summary>
    public void Archive() => IsArchived = true;

    /// <summary>Brings an archived class back into use.</summary>
    public void Restore() => IsArchived = false;

    /// <summary>Checks a class name and returns it trimmed, so a caller can test it for clashes before the class is made or renamed.</summary>
    /// <param name="name">The name as given.</param>
    /// <exception cref="InvalidClassError">The name is blank or too long.</exception>
    public static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidClassError("A class needs a name.");
        if (trimmed.Length > MaxNameLength)
            throw new InvalidClassError($"A class name must be at most {MaxNameLength} characters.");
        return trimmed;
    }
}
