using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IExtraAttemptGrantRepository"/>.</summary>
public sealed class ExtraAttemptGrantRepository(ExamRuntimeDbContext context) : IExtraAttemptGrantRepository
{
    /// <inheritdoc />
    public void Add(ExtraAttemptGrant grant) => context.ExtraAttemptGrants.Add(grant);

    /// <inheritdoc />
    public Task<int> CountAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken) =>
        context.ExtraAttemptGrants.CountAsync(g => g.ExamId == examId && g.CandidateId == candidateId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountsForCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
        await context.ExtraAttemptGrants.AsNoTracking()
            .Where(g => g.CandidateId == candidateId)
            .GroupBy(g => g.ExamId)
            .Select(group => new { ExamId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.ExamId, row => row.Count, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountsForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        await context.ExtraAttemptGrants.AsNoTracking()
            .Where(g => g.ExamId == examId)
            .GroupBy(g => g.CandidateId)
            .Select(group => new { CandidateId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.CandidateId, row => row.Count, cancellationToken);
}
