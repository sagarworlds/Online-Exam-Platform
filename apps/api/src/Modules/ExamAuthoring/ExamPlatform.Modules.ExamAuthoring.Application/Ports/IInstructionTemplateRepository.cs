using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Ports;

/// <summary>Persistence port for <see cref="InstructionTemplate"/>s (FR-41).</summary>
public interface IInstructionTemplateRepository
{
    /// <summary>Begins tracking a new template for insertion on the next save.</summary>
    /// <param name="template">The template to add.</param>
    void Add(InstructionTemplate template);

    /// <summary>Begins tracking a template's removal on the next save.</summary>
    /// <param name="template">The template to remove.</param>
    void Remove(InstructionTemplate template);

    /// <summary>Loads a template for a change.</summary>
    /// <param name="templateId">The template's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tracked template.</returns>
    /// <exception cref="Domain.Exceptions.InstructionTemplateNotFoundError">No template has that id.</exception>
    Task<InstructionTemplate> GetOrThrowAsync(Guid templateId, CancellationToken cancellationToken);

    /// <summary>Lists every template, by title.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The templates, read-only.</returns>
    Task<IReadOnlyList<InstructionTemplate>> ListAsync(CancellationToken cancellationToken);
}
