using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Creates a class, the level above books.</summary>
/// <param name="Name">The class's name.</param>
/// <param name="CreatedBy">The authoring user, taken from the caller's token.</param>
public sealed record CreateClassCommand(string? Name, Guid CreatedBy);

/// <summary>Renames a class.</summary>
/// <param name="ClassId">The class to rename.</param>
/// <param name="Name">The new name.</param>
public sealed record RenameClassCommand(Guid ClassId, string? Name);

/// <summary>Creates a class.</summary>
public sealed class CreateClassHandler(IClassRepository classes, IQuestionBankUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Validates and stores the class.</summary>
    /// <param name="command">The class to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored class, with no books yet.</returns>
    /// <exception cref="InvalidClassError">The name is blank or too long.</exception>
    /// <exception cref="DuplicateClassError">Another class already has that name, ignoring case.</exception>
    public async Task<ClassDto> HandleAsync(CreateClassCommand command, CancellationToken cancellationToken)
    {
        var schoolClass = SchoolClass.Create(command.Name, command.CreatedBy, clock.UtcNow);
        if (await classes.NameIsTakenAsync(schoolClass.Name, exceptClassId: null, cancellationToken))
            throw new DuplicateClassError(schoolClass.Name);

        classes.Add(schoolClass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return schoolClass.ToDto(bookCount: 0);
    }
}

/// <summary>Runs one change on a class and returns the class as it is afterwards.</summary>
public sealed class ChangeClassHandler(IClassRepository classes, IQuestionBankUnitOfWork unitOfWork)
{
    /// <summary>Renames a class.</summary>
    /// <param name="command">The rename.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ClassNotFoundError">No class has that id.</exception>
    /// <exception cref="InvalidClassError">The name is blank or too long.</exception>
    /// <exception cref="DuplicateClassError">Another class already has that name, ignoring case.</exception>
    public Task<ClassDto> RenameAsync(RenameClassCommand command, CancellationToken cancellationToken) =>
        ChangeAsync(
            command.ClassId,
            async schoolClass =>
            {
                var name = SchoolClass.ValidateName(command.Name);
                if (await classes.NameIsTakenAsync(name, schoolClass.Id, cancellationToken))
                    throw new DuplicateClassError(name);

                schoolClass.Rename(name);
            },
            cancellationToken);

    /// <summary>Archives a class; it keeps its books but takes no new ones.</summary>
    /// <param name="classId">The class to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ClassNotFoundError">No class has that id.</exception>
    public Task<ClassDto> ArchiveAsync(Guid classId, CancellationToken cancellationToken) =>
        ChangeAsync(classId, schoolClass => { schoolClass.Archive(); return Task.CompletedTask; }, cancellationToken);

    /// <summary>Brings an archived class back into use.</summary>
    /// <param name="classId">The class to restore.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ClassNotFoundError">No class has that id.</exception>
    public Task<ClassDto> RestoreAsync(Guid classId, CancellationToken cancellationToken) =>
        ChangeAsync(classId, schoolClass => { schoolClass.Restore(); return Task.CompletedTask; }, cancellationToken);

    private async Task<ClassDto> ChangeAsync(Guid classId, Func<SchoolClass, Task> change, CancellationToken cancellationToken)
    {
        var schoolClass = await classes.GetByIdAsync(classId, cancellationToken) ?? throw new ClassNotFoundError();

        await change(schoolClass);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await classes.CountBooksAsync(cancellationToken);
        return schoolClass.ToDto(counts.GetValueOrDefault(schoolClass.Id));
    }
}
