using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IAccommodationRepository"/>.</summary>
public sealed class AccommodationRepository(ExamRuntimeDbContext context) : IAccommodationRepository
{
    /// <inheritdoc />
    public void Add(Accommodation accommodation) => context.Accommodations.Add(accommodation);

    /// <inheritdoc />
    public void Remove(Accommodation accommodation) => context.Accommodations.Remove(accommodation);

    // Tracked, because this is the load used to change one.
    /// <inheritdoc />
    public Task<Accommodation?> FindAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken) =>
        context.Accommodations.FirstOrDefaultAsync(a => a.ExamId == examId && a.CandidateId == candidateId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, Accommodation>> ListForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        await context.Accommodations.AsNoTracking().Where(a => a.ExamId == examId).ToDictionaryAsync(a => a.CandidateId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, Accommodation>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
        await context.Accommodations.AsNoTracking().Where(a => a.CandidateId == candidateId).ToDictionaryAsync(a => a.ExamId, cancellationToken);
}
