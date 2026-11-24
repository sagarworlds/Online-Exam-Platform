using ExamPlatform.Modules.ExamAuthoring.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Ports;

/// <summary>
/// Keeps the <see cref="ExamSnapshot"/> of an exam for a few seconds. Read through <see cref="CachedExamCatalog"/> and dropped by
/// <see cref="SnapshotInvalidatingUnitOfWork"/> whenever an exam is changed.
/// </summary>
/// <remarks>
/// A reader takes <see cref="Generation"/> before it reads the exam and hands it back to <see cref="Set"/>; <see cref="Clear"/> starts a new
/// generation, so a read that was overtaken by a change can never put its older result back.
/// </remarks>
public interface IExamSnapshotCache
{
    /// <summary>The generation now. A reader takes it before it reads the exam and hands it back to <see cref="Set"/>.</summary>
    long Generation { get; }

    /// <summary>Looks up an exam's snapshot.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="snapshot">The snapshot, when one is held that is still fresh and was read after the last change.</param>
    /// <returns><see langword="true"/> when a snapshot is returned.</returns>
    bool TryGet(Guid examId, out ExamSnapshot? snapshot);

    /// <summary>Keeps a snapshot that was read while the cache was at <paramref name="generation"/>.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="snapshot">What was read.</param>
    /// <param name="generation">The value of <see cref="Generation"/> from before the read began.</param>
    void Set(Guid examId, ExamSnapshot snapshot, long generation);

    /// <summary>Forgets every snapshot, because an exam has been changed.</summary>
    void Clear();
}
