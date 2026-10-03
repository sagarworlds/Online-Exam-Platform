namespace ExamPlatform.SharedKernel.Application;

/// <summary>Chooses which of some candidates a draw takes. A port so a test can make the choice predictable.</summary>
public interface IQuestionPicker
{
    /// <summary>Picks <paramref name="count"/> of the items at random, each at most once.</summary>
    /// <typeparam name="T">The kind of item.</typeparam>
    /// <param name="items">The candidates.</param>
    /// <param name="count">How many to pick; not more than there are items.</param>
    /// <returns>The picked items in the order they were picked.</returns>
    IReadOnlyList<T> Pick<T>(IReadOnlyList<T> items, int count);
}

/// <summary>Picks uniformly at random. Draws are never replayed, so it needs no seed or reproducibility.</summary>
public sealed class RandomQuestionPicker : IQuestionPicker
{
    /// <inheritdoc />
    public IReadOnlyList<T> Pick<T>(IReadOnlyList<T> items, int count)
    {
        // Partial Fisher-Yates: only the first `count` positions are settled, so a draw of 10 from 5000 does 10 swaps.
        var pool = items.ToArray();
        for (var i = 0; i < count; i++)
        {
            var j = Random.Shared.Next(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.Take(count).ToList();
    }
}
