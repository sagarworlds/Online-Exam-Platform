using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// An <see cref="IExamCatalog"/> that serves one exam's snapshot from <see cref="IExamSnapshotCache"/>, which every call a candidate makes
/// during an exam would otherwise pay four queries for. The other reads are not kept: they ask about several exams or about a window of time,
/// and what they return (which exams are published, which are about to start) must be current.
/// </summary>
public sealed class CachedExamCatalog(IExamCatalog inner, IExamSnapshotCache cache) : IExamCatalog
{
    /// <inheritdoc />
    /// <remarks>The generation is taken before the read so a change made while it runs is never overwritten by the stale result.</remarks>
    public async Task<ExamSnapshot?> FindAsync(Guid examId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(examId, out var kept))
            return kept;

        var generation = cache.Generation;
        var snapshot = await inner.FindAsync(examId, cancellationToken);
        if (snapshot is not null)
            cache.Set(examId, snapshot, generation);

        return snapshot;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ExamSnapshot>> FindPublishedAsync(IReadOnlyCollection<Guid> examIds, CancellationToken cancellationToken) =>
        inner.FindPublishedAsync(examIds, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ExamSnapshot>> FindPublishedStartingBetweenAsync(DateTime afterUtc, DateTime untilUtc, CancellationToken cancellationToken) =>
        inner.FindPublishedStartingBetweenAsync(afterUtc, untilUtc, cancellationToken);
}
