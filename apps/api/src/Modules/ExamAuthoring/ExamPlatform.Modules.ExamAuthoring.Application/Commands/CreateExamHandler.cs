using MediatR;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// Handler for CreateExamCommand. Creates a new exam aggregate with default config.
public class CreateExamHandler(IExamRepository examRepository, IExamAuthoringUnitOfWork unitOfWork) : IRequestHandler<CreateExamCommand, ExamDto>
{
    public async Task<ExamDto> Handle(CreateExamCommand command, CancellationToken cancellationToken)
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
