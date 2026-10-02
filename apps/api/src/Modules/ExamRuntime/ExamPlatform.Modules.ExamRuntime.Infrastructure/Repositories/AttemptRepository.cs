using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IAttemptRepository"/>.</summary>
public sealed class AttemptRepository(ExamRuntimeDbContext context) : IAttemptRepository
{
    /// <inheritdoc />
    public void Add(Attempt attempt) => context.Attempts.Add(attempt);

    // Tracked, with answers and marks loaded, on purpose: the aggregate's rules read them, and handlers rely on
    // change tracking to INSERT, UPDATE or DELETE them; an explicit DbSet.Update would flag every answer Modified.
    /// <inheritdoc />
    public Task<Attempt?> GetByIdAsync(Guid attemptId, CancellationToken cancellationToken) =>
        context.Attempts.Include(a => a.Answers).Include(a => a.Marks).FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListForCandidateAtExamAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken) =>
        // Starting an exam again resumes an open attempt through this list, and what it shows is built from the attempt's
        // answers and marks, so both are loaded: without the marks a resumed exam would forget what the candidate marked.
        await context.Attempts.Include(a => a.Answers).Include(a => a.Marks)
            .Where(a => a.ExamId == examId && a.CandidateId == candidateId)
            .OrderBy(a => a.Number)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        await context.Attempts.AsNoTracking().Where(a => a.ExamId == examId).OrderBy(a => a.Number).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> FindAnsweredQuestionIdsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
        await context.Set<AttemptAnswer>().AsNoTracking()
            .Where(a => questionIds.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
        await context.Attempts.AsNoTracking().Where(a => a.CandidateId == candidateId).ToListAsync(cancellationToken);
}
