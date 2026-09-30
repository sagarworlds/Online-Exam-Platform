using MediatR;
using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// Command to create a new exam.
public record CreateExamCommand(
    Guid SeriesId,
    string Name,
    string? Description,
    Guid CreatedBy
) : IRequest<ExamDto>;
