using System.Collections.Concurrent;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Application;

/// <summary>
/// Keeps the <see cref="ExamSnapshot"/> of an exam for a few seconds, so a candidate's save, status check or page read does not rebuild
/// the whole exam from four queries each time.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is an immutable record, so sharing one between requests is safe. It goes stale only when an exam is changed, and every change
/// goes through an <see cref="IExamAuthoringUnitOfWork"/>, which <see cref="SnapshotInvalidatingUnitOfWork"/> wraps to call <see cref="Clear"/> when it is done: on the host that
/// made the change the next read sees it at once. Another host has its own cache and sees the change when its entry expires, so the lifetime
/// is the longest a change can be unseen there.
/// </para>
/// <para>
/// <see cref="Clear"/> also starts a new generation. A read that began before the change and finishes after it still holds the old
/// generation, so what it stores is never served: the clear cannot be undone by a slow reader.
/// </para>
/// </remarks>
public sealed class ExamSnapshotCache(Clock clock, TimeSpan lifetime, int maxEntries) : IExamSnapshotCache
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private long _generation;

    private sealed record Entry(ExamSnapshot Snapshot, DateTime ExpiresAtUtc, long Generation);

    /// <inheritdoc />
    public long Generation => Volatile.Read(ref _generation);

    /// <summary>Whether snapshots are kept at all: a lifetime of zero turns the cache off.</summary>
    public bool IsEnabled => lifetime > TimeSpan.Zero && maxEntries > 0;

    /// <inheritdoc />
    public bool TryGet(Guid examId, out ExamSnapshot? snapshot)
    {
        if (IsEnabled
            && _entries.TryGetValue(examId, out var entry)
            && entry.Generation == Generation
            && entry.ExpiresAtUtc > clock.UtcNow)
        {
            snapshot = entry.Snapshot;
            return true;
        }

        snapshot = null;
        return false;
    }

    /// <inheritdoc />
    public void Set(Guid examId, ExamSnapshot snapshot, long generation)
    {
        if (!IsEnabled || generation != Generation)
            return;

        // A bound, not a policy: the number of exams a host serves at once is small, so reaching it means something is wrong, and the answer
        // is to start again rather than to choose which exam to forget.
        if (_entries.Count >= maxEntries)
            _entries.Clear();

        _entries[examId] = new Entry(snapshot, clock.UtcNow + lifetime, generation);
    }

    /// <inheritdoc />
    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _entries.Clear();
    }
}
