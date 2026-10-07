using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>Checks that a class a book is to be put under exists and can take books.</summary>
public sealed class OpenClassResolver(IClassRepository classes)
{
    /// <summary>Looks the class up. An archived class keeps the books it has but takes nothing new, which is what archiving is for.</summary>
    /// <param name="classId">The class, or null for a book that is under none (always allowed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The class's name, or null when no class was asked for.</returns>
    /// <exception cref="ClassNotFoundError">No class has that id.</exception>
    /// <exception cref="ClassArchivedError">The class is archived.</exception>
    public async Task<string?> ResolveAsync(Guid? classId, CancellationToken cancellationToken)
    {
        if (classId is not { } id)
            return null;

        var schoolClass = await classes.GetByIdAsync(id, cancellationToken) ?? throw new ClassNotFoundError();
        if (schoolClass.IsArchived)
            throw new ClassArchivedError($"The class \"{schoolClass.Name}\" is archived; restore it or choose another.");

        return schoolClass.Name;
    }
}
