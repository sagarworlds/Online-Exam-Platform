using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Handles <see cref="CreateInstructionTemplateCommand"/> (FR-41).</summary>
public sealed class CreateInstructionTemplateHandler(
    IInstructionTemplateRepository repository,
    IExamAuthoringUnitOfWork unitOfWork,
    InstructionAudit audit,
    Clock clock)
{
    /// <summary>Creates a template.</summary>
    /// <param name="command">The title and text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new template.</returns>
    /// <exception cref="Domain.Exceptions.InvalidInstructionTemplateError">The title or text is blank or too long. Nothing is saved.</exception>
    public async Task<InstructionTemplateDto> HandleAsync(CreateInstructionTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = InstructionTemplate.Create(command.Title, command.Body, clock.UtcNow);
        repository.Add(template);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            "ExamAuthoring.InstructionTemplateCreated",
            nameof(InstructionTemplate),
            template.Id,
            new Dictionary<string, string> { ["title"] = template.Title },
            cancellationToken);

        return InstructionTemplateDto.From(template);
    }
}

/// <summary>Handles <see cref="UpdateInstructionTemplateCommand"/> (FR-41).</summary>
public sealed class UpdateInstructionTemplateHandler(
    IInstructionTemplateRepository repository,
    IExamAuthoringUnitOfWork unitOfWork,
    InstructionAudit audit,
    Clock clock)
{
    /// <summary>Changes a template.</summary>
    /// <param name="command">The template and its new title and text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The template as it now is.</returns>
    /// <exception cref="Domain.Exceptions.InstructionTemplateNotFoundError">No template has that id.</exception>
    /// <exception cref="Domain.Exceptions.InvalidInstructionTemplateError">The title or text is blank or too long. Nothing is saved.</exception>
    public async Task<InstructionTemplateDto> HandleAsync(UpdateInstructionTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await repository.GetOrThrowAsync(command.TemplateId, cancellationToken);
        template.Change(command.Title, command.Body, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            "ExamAuthoring.InstructionTemplateUpdated",
            nameof(InstructionTemplate),
            template.Id,
            new Dictionary<string, string> { ["title"] = template.Title },
            cancellationToken);

        return InstructionTemplateDto.From(template);
    }
}

/// <summary>Handles <see cref="DeleteInstructionTemplateCommand"/> (FR-41).</summary>
public sealed class DeleteInstructionTemplateHandler(
    IInstructionTemplateRepository repository,
    IExamAuthoringUnitOfWork unitOfWork,
    InstructionAudit audit)
{
    /// <summary>
    /// Deletes a template. Safe while exams use it: an exam keeps the text it copied, so nothing refers to the template by id.
    /// </summary>
    /// <param name="command">The template to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Domain.Exceptions.InstructionTemplateNotFoundError">No template has that id.</exception>
    public async Task HandleAsync(DeleteInstructionTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await repository.GetOrThrowAsync(command.TemplateId, cancellationToken);
        repository.Remove(template);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            "ExamAuthoring.InstructionTemplateDeleted",
            nameof(InstructionTemplate),
            template.Id,
            new Dictionary<string, string> { ["title"] = template.Title },
            cancellationToken);
    }
}
