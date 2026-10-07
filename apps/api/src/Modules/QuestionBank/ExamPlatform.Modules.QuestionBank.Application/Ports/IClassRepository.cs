using ExamPlatform.Modules.QuestionBank.Domain;

namespace ExamPlatform.Modules.QuestionBank.Application.Ports;

/// <summary>Persistence port for <see cref="SchoolClass"/>.</summary>
public interface IClassRepository
{
    /// <summary>Starts tracking a new class; it is stored when the unit of work saves.</summary>
    /// <param name="schoolClass">The class to add.</param>
    void Add(SchoolClass schoolClass);

    /// <summary>Loads one class.</summary>
    /// <param name="classId">The class's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The class, or <see langword="null"/> when none has that id.</returns>
    Task<SchoolClass?> GetByIdAsync(Guid classId, CancellationToken cancellationToken);

    /// <summary>Lists classes ordered by name.</summary>
    /// <param name="includeArchived">Whether archived classes are included.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SchoolClass>> ListAsync(bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Whether a class other than <paramref name="exceptClassId"/> already has this name, ignoring case.</summary>
    /// <param name="name">The trimmed name to look for.</param>
    /// <param name="exceptClassId">A class to leave out, so renaming a class to its own name is not a clash; null leaves none out.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> NameIsTakenAsync(string name, Guid? exceptClassId, CancellationToken cancellationToken);

    /// <summary>Reads class names by id in one query, archived classes included (a book keeps showing the class it is in).</summary>
    /// <param name="classIds">The ids to read; unknown ids are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> classIds, CancellationToken cancellationToken);

    /// <summary>Counts the books of every class that has any, archived books included.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count by class id; a class with no books is absent.</returns>
    Task<IReadOnlyDictionary<Guid, int>> CountBooksAsync(CancellationToken cancellationToken);
}
