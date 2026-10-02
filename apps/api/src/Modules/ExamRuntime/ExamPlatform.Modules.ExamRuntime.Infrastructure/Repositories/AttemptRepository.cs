using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IAttemptRepository"/>.</summary>
public sealed class AttemptRepository(ExamRuntimeDbContext context) : IAttemptRepository
{
    /// <inheritdoc />
    public void Add(Attempt attempt) => context.Attempts.Add(attempt);

    // Tracked, with answers loaded, on purpose: the aggregate's rules read its answers, and handlers rely on
    // change tracking to INSERT or UPDATE them; an explicit DbSet.Update would flag every answer Modified.
    /// <inheritdoc />
    public Task<Attempt?> GetByIdAsync(Guid attemptId, CancellationToken cancellationToken) =>
        context.Attempts.Include(a => a.Answers).FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

    /// <inheritdoc />
    public Task<Attempt?> FindAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken) =>
        context.Attempts.Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.ExamId == examId && a.CandidateId == candidateId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
        await context.Attempts.AsNoTracking().Where(a => a.CandidateId == candidateId).ToListAsync(cancellationToken);
}
