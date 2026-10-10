using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Handles <see cref="SetExamInstructionsCommand"/>: sets an exam's instructions by hand (FR-41).</summary>
public sealed class SetExamInstructionsHandler(
    IExamRepository repository,
    IExamAuthoringUnitOfWork unitOfWork,
    ExamDtoFactory dtos,
    InstructionAudit audit,
    Clock clock)
{
    /// <summary>Sets the instructions text of a draft exam.</summary>
    /// <param name="command">The exam and its new text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exam as it now is.</returns>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The text is longer than the limit. Nothing is saved.</exception>
    public async Task<ExamDto> HandleAsync(SetExamInstructionsCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.SetInstructions(command.Instructions, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            "ExamAuthoring.ExamInstructionsChanged",
            nameof(Exam),
            exam.Id,
            new Dictionary<string, string> { ["source"] = "manual" },
            cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>
/// Handles <see cref="UseInstructionTemplateCommand"/>: starts an exam's instructions from a template by copying its text (FR-41). The copy
/// is the exam's own, so later edits to the template do not reach the exam.
/// </summary>
public sealed class UseInstructionTemplateHandler(
    IExamRepository examRepository,
    IInstructionTemplateRepository templateRepository,
    IExamAuthoringUnitOfWork unitOfWork,
    ExamDtoFactory dtos,
    InstructionAudit audit,
    Clock clock)
{
    /// <summary>Copies a template's text into a draft exam's instructions, replacing what was there.</summary>
    /// <param name="command">The exam and the template to copy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exam as it now is.</returns>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InstructionTemplateNotFoundError">No template has that id.</exception>
    public async Task<ExamDto> HandleAsync(UseInstructionTemplateCommand command, CancellationToken cancellationToken)
    {
        var exam = await examRepository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        var template = await templateRepository.GetOrThrowAsync(command.TemplateId, cancellationToken);
        exam.SetInstructions(template.Body, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            "ExamAuthoring.ExamInstructionsChanged",
            nameof(Exam),
            exam.Id,
            new Dictionary<string, string>
            {
                ["source"] = "template",
                ["templateId"] = template.Id.ToString(),
            },
            cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}
