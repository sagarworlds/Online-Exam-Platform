using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IClassRepository"/>.</summary>
public sealed class ClassRepository(QuestionBankDbContext context) : IClassRepository
{
    /// <inheritdoc />
    public void Add(SchoolClass schoolClass) => context.Classes.Add(schoolClass);

    /// <inheritdoc />
    public Task<SchoolClass?> GetByIdAsync(Guid classId, CancellationToken cancellationToken) =>
        context.Classes.FirstOrDefaultAsync(c => c.Id == classId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SchoolClass>> ListAsync(bool includeArchived, CancellationToken cancellationToken) =>
        await context.Classes.AsNoTracking()
            .Where(c => includeArchived || !c.IsArchived)
            .OrderBy(c => c.Name).ThenBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> NameIsTakenAsync(string name, Guid? exceptClassId, CancellationToken cancellationToken)
    {
        // Compared in lower case so "4th" and "4TH" are one class; the database's own unique index only catches an exact repeat.
        var lowered = name.ToLower();
        return context.Classes.AsNoTracking()
            .AnyAsync(c => c.Id != exceptClassId && c.Name.ToLower() == lowered, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> classIds, CancellationToken cancellationToken)
    {
        if (classIds.Count == 0)
            return new Dictionary<Guid, string>();

        return await context.Classes.AsNoTracking()
            .Where(c => classIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountBooksAsync(CancellationToken cancellationToken) =>
        await context.Books.AsNoTracking()
            .Where(b => b.ClassId != null)
            .GroupBy(b => b.ClassId!.Value)
            .Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ClassId, x => x.Count, cancellationToken);
}
