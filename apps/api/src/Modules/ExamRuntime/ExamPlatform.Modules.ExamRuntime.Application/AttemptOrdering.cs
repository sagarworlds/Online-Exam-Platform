using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// The order a candidate sees questions and options in. The first attempt shows them as the author wrote them unless the author turned shuffling on; every later
/// attempt shows them shuffled, so someone sitting the exam again cannot lean on where an answer used to be. The same rule
/// is used for what the candidate sits and for the review of it, so "question 3" means the same thing in both.
/// </summary>
/// <remarks>
/// The shuffle is a function of the attempt, not of chance at the moment it is asked for: reloading, resuming on another
/// device and reviewing later all show the order the candidate originally saw, with nothing to store. It is built from SHA-256
/// rather than a random number generator, so it cannot change when the framework's generator does, and it is guaranteed to
/// differ from the authored order whenever there are at least two items, so a shuffled attempt never looks unshuffled.
/// </remarks>
public static class AttemptOrdering
{
    /// <summary>How many times a shuffle that came out as the authored order is tried again before the order is simply reversed.</summary>
    private const int MaxRetries = 8;

    /// <summary>Whether an attempt shows things in a shuffled order: every attempt after the first, and the first too when the author asked.</summary>
    /// <param name="attemptNumber">Which attempt this is for the candidate at the exam, from 1.</param>
    /// <param name="authorShuffles">Whether the exam's author turned shuffling on for this kind of item.</param>
    public static bool IsShuffled(int attemptNumber, bool authorShuffles = false) => authorShuffles || attemptNumber >= 2;

    /// <summary>Puts items in the order the attempt shows them.</summary>
    /// <typeparam name="T">What is being ordered: a question id, or an option.</typeparam>
    /// <param name="authored">The items in the order the author wrote them.</param>
    /// <param name="idOf">An item's id, which the shuffle is keyed on, so the same item lands in the same place however it is asked for.</param>
    /// <param name="attemptId">The attempt, which makes the shuffle different for each attempt.</param>
    /// <param name="attemptNumber">Which attempt this is for the candidate at the exam, from 1; the first is not shuffled.</param>
    /// <param name="scopeId">What the items belong to (a section's id for its questions, a question's id for its options), so each list is shuffled independently.</param>
    /// <param name="authorShuffles">Whether the exam's author turned shuffling on for this kind of item, which shuffles the first attempt as well.</param>
    /// <param name="isPinned">
    /// Says which items keep the position the author gave them, such as a "none of the above" that must stay last. Null means
    /// nothing is pinned. The other items are shuffled among the positions the pinned ones leave free.
    /// </param>
    /// <returns>The authored list itself for an unshuffled attempt or fewer than two movable items; otherwise a shuffled copy that differs from it.</returns>
    public static IReadOnlyList<T> Arrange<T>(
        IReadOnlyList<T> authored, Func<T, Guid> idOf, Guid attemptId, int attemptNumber, Guid scopeId, bool authorShuffles = false, Func<T, bool>? isPinned = null)
    {
        if (!IsShuffled(attemptNumber, authorShuffles))
            return authored;

        if (isPinned is null)
            return Shuffle(authored, idOf, attemptId, scopeId);

        // Only the movable items are shuffled, then dealt back into the positions the pinned items do not occupy, so a pinned
        // item's place never depends on the shuffle.
        var movable = authored.Where(item => !isPinned(item)).ToList();
        var shuffled = Shuffle(movable, idOf, attemptId, scopeId);
        if (ReferenceEquals(shuffled, movable))
            return authored;

        var next = 0;
        return authored.Select(item => isPinned(item) ? item : shuffled[next++]).ToList();
    }

    private static IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> authored, Func<T, Guid> idOf, Guid attemptId, Guid scopeId)
    {
        if (authored.Count < 2)
            return authored;

        var authoredIds = authored.Select(idOf).ToList();
        for (var retry = 0; retry < MaxRetries; retry++)
        {
            var arranged = authored.OrderBy(item => Rank(attemptId, scopeId, idOf(item), retry)).ThenBy(idOf).ToList();
            if (!arranged.Select(idOf).SequenceEqual(authoredIds))
                return arranged;
        }

        // Astronomically unlikely, but "shuffled" must never mean "unchanged", and reversing is always different for two or more distinct items.
        return authored.Reverse().ToList();
    }

    /// <summary>A number that is fixed for an item in an attempt but looks random between items, to sort by.</summary>
    private static ulong Rank(Guid attemptId, Guid scopeId, Guid itemId, int retry)
    {
        Span<byte> input = stackalloc byte[16 * 3 + sizeof(int)];
        attemptId.TryWriteBytes(input[..16]);
        scopeId.TryWriteBytes(input.Slice(16, 16));
        itemId.TryWriteBytes(input.Slice(32, 16));
        BinaryPrimitives.WriteInt32BigEndian(input[48..], retry);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }
}
