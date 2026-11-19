using System.Collections.Concurrent;
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
/// goes through <see cref="IExamAuthoringUnitOfWork.SaveChangesAsync"/>, which calls <see cref="Clear"/> when it is done: on the host that
/// made the change the next read sees it at once. Another host has its own cache and sees the change when its entry expires, so the lifetime
/// is the longest a change can be unseen there.
/// </para>
/// <para>
/// <see cref="Clear"/> also starts a new generation. A read that began before the change and finishes after it still holds the old
/// generation, so what it stores is never served: the clear cannot be undone by a slow reader.
/// </para>
/// </remarks>
public sealed class ExamSnapshotCache(Clock clock, TimeSpan lifetime, int maxEntries)
{
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private long _generation;

    private sealed record Entry(ExamSnapshot Snapshot, DateTime ExpiresAtUtc, long Generation);

    /// <summary>The generation now. A reader takes it before it reads the exam and hands it back to <see cref="Set"/>.</summary>
    public long Generation => Volatile.Read(ref _generation);

    /// <summary>Whether snapshots are kept at all: a lifetime of zero turns the cache off.</summary>
    public bool IsEnabled => lifetime > TimeSpan.Zero && maxEntries > 0;

    /// <summary>Looks up an exam's snapshot.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="snapshot">The snapshot, when one is held that is still fresh and was read after the last change.</param>
    /// <returns><see langword="true"/> when a snapshot is returned.</returns>
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

    /// <summary>Keeps a snapshot that was read while the cache was at <paramref name="generation"/>.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="snapshot">What was read.</param>
    /// <param name="generation">The value of <see cref="Generation"/> from before the read began.</param>
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

    /// <summary>Forgets every snapshot, because an exam has been changed.</summary>
    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _entries.Clear();
    }
}
