using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IAttemptRequestRepository"/>.</summary>
public sealed class AttemptRequestRepository(ExamRuntimeDbContext context) : IAttemptRequestRepository
{
    /// <inheritdoc />
    public void Add(AttemptRequest request) => context.AttemptRequests.Add(request);

    /// <inheritdoc />
    public Task<AttemptRequest?> GetByIdAsync(Guid requestId, CancellationToken cancellationToken) =>
        context.AttemptRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasPendingAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken) =>
        context.AttemptRequests.AnyAsync(
            r => r.ExamId == examId && r.CandidateId == candidateId && r.Status == AttemptRequestStatus.Pending, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AttemptRequest>> ListAsync(AttemptRequestStatus status, int take, CancellationToken cancellationToken) =>
        await context.AttemptRequests
            .Where(r => r.Status == status)
            .OrderBy(r => r.RequestedAtUtc).ThenBy(r => r.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, AttemptRequest>> LatestForCandidateAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var all = await context.AttemptRequests.AsNoTracking().Where(r => r.CandidateId == candidateId).ToListAsync(cancellationToken);

        // A candidate has a handful of requests at most, so choosing the latest per exam in memory is simpler than a grouped query.
        return all.GroupBy(r => r.ExamId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RequestedAtUtc).First());
    }
}
