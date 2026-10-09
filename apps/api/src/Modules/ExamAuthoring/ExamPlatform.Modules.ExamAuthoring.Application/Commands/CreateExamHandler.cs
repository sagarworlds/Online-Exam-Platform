using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Handles <see cref="CreateExamCommand"/>: creates a new exam aggregate with the default configuration.</summary>
public sealed class CreateExamHandler(
    IExamRepository examRepository,
    IExamAuthoringUnitOfWork unitOfWork,
    ExamScopeResolver scopeResolver,
    ExamDtoFactory dtos)
{
    /// <summary>Creates the exam and persists it.</summary>
    /// <param name="command">The exam to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created exam.</returns>
    /// <exception cref="InvalidExamConfigError">
    /// <see cref="CreateExamCommand.SeriesId"/> is the empty GUID, the name is blank or too long, the description is too long,
    /// or the scope names a book or chapters that cannot be used.
    /// </exception>
    public async Task<ExamDto> HandleAsync(CreateExamCommand command, CancellationToken cancellationToken)
    {
        // A blank series is "no series" (null). The empty GUID is what a form posts when it
        // converts a blank field, and it would be stored as a series that does not exist,
        // so it is refused with a 400 instead of being accepted silently.
        if (command.SeriesId == Guid.Empty)
            throw new InvalidExamConfigError("SeriesId must be omitted or a non-empty GUID.");

        // Resolved before anything is created, so a bad scope leaves nothing behind.
        var scope = await scopeResolver.ResolveAsync(command.Scope, cancellationToken);

        var exam = new Exam(
            command.SeriesId,
            command.Name,
            command.Description,
            Exam.NotScheduledAt, // Scheduling is set afterwards, by ScheduleExamHandler
            Exam.NotScheduledAt,
            command.CreatedBy
        );

        // A new exam holds no questions yet, so there is nothing for the scope to leave outside it.
        exam.SetScope(scope, new Dictionary<Guid, QuestionPlacement>());

        examRepository.Add(exam);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}
