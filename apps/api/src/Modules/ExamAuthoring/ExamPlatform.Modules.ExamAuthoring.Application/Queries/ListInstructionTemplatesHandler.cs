using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Queries;

/// <summary>Handles the listing of instruction templates (FR-41).</summary>
public sealed class ListInstructionTemplatesHandler(IInstructionTemplateRepository repository)
{
    /// <summary>Lists every template, by title, so staff choose from them in a stable order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The templates.</returns>
    public async Task<IReadOnlyList<InstructionTemplateDto>> HandleAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Select(InstructionTemplateDto.From).ToList();
}
