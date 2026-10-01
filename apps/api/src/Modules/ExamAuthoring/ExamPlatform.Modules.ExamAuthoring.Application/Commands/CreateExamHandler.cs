using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Handles <see cref="CreateExamCommand"/>: creates a new exam aggregate with the default configuration.</summary>
public sealed class CreateExamHandler(IExamRepository examRepository, IExamAuthoringUnitOfWork unitOfWork)
{
    /// <summary>Creates the exam and persists it.</summary>
    /// <param name="command">The exam to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created exam.</returns>
    /// <exception cref="ArgumentException"><see cref="CreateExamCommand.Name"/> is empty or whitespace.</exception>
    public async Task<ExamDto> HandleAsync(CreateExamCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Name cannot be empty or whitespace", nameof(command.Name));

        var exam = new Exam(
            command.SeriesId,
            command.Name,
            command.Description,
            DateTime.MinValue, // Scheduling is set via separate ScheduleExamCommand
            DateTime.MinValue,
            command.CreatedBy
        );

        examRepository.Add(exam);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(exam);
    }

    private static ExamDto MapToDto(Exam exam) =>
        new(
            exam.Id,
            exam.SeriesId,
            exam.Name,
            exam.Description,
            exam.Status,
            new ExamConfigDto(
                exam.Config.TotalTimeSeconds,
                exam.Config.ShuffleQuestions,
                exam.Config.ShuffleOptions,
                exam.Config.SectionLockEnabled,
                exam.Config.CalculatorAllowed,
                exam.Config.ScratchpadAllowed,
                exam.Config.MaxAttempts,
                exam.Config.MaxRetakes,
                exam.Config.ResultReleaseMode,
                exam.Config.ResultReleaseTime,
                exam.Config.MarkingScheme
            ),
            exam.ScheduledStartTime,
            exam.ScheduledEndTime,
            exam.LateEntryDeadline,
            exam.TimeZone,
            exam.CreatedBy,
            exam.CreatedAt,
            exam.UpdatedAt
        );
}
