using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IInstructionTemplateRepository"/> (FR-41).</summary>
public sealed class EFInstructionTemplateRepository(ExamAuthoringDbContext context) : IInstructionTemplateRepository
{
    /// <inheritdoc />
    public void Add(InstructionTemplate template) => context.InstructionTemplates.Add(template);

    /// <inheritdoc />
    public void Remove(InstructionTemplate template) => context.InstructionTemplates.Remove(template);

    /// <inheritdoc />
    public async Task<InstructionTemplate> GetOrThrowAsync(Guid templateId, CancellationToken cancellationToken) =>
        await context.InstructionTemplates.FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
        ?? throw new InstructionTemplateNotFoundError(templateId);

    // Read without tracking: a listing is only shown, and a template is loaded for a change by GetOrThrowAsync. Titles sort first so
    // staff find a template by name in a stable order.
    /// <inheritdoc />
    public async Task<IReadOnlyList<InstructionTemplate>> ListAsync(CancellationToken cancellationToken) =>
        await context.InstructionTemplates
            .AsNoTracking()
            .OrderBy(t => t.Title)
            .ToListAsync(cancellationToken);
}
