namespace ExamPlatform.Modules.ExamRuntime.Application.Grading;

/// <summary>
/// The nudge that wakes the host's grading worker: given when typed answers have been queued for the AI evaluator, so the worker looks at the
/// queue then and not on a timer. With nothing queued the worker sleeps and the database is left alone (a free Neon database suspends when it
/// is not queried, and a worker that looked every few seconds kept it on all month).
/// </summary>
/// <remarks>
/// A single standing signal, not a count: many nudges before the worker looks are one wake-up, because one look takes every answer that is due.
/// One host only. Another host sharing the database has its own worker, which finds what is queued at its next look (start-up, or the
/// next time something it is waiting for falls due), and the lease on each answer keeps the two from asking about the same one.
/// </remarks>
public sealed class GradingWakeUp
{
    // The longest one wait may be; the semaphore cannot wait longer than about 24 days, and a day is far past anything the worker waits for.
    private static readonly TimeSpan LongestWait = TimeSpan.FromDays(1);

    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>Wakes the worker, or leaves the wake-up already waiting for it. Safe to call from any thread, at any time.</summary>
    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already waiting: one is all the worker needs.
        }
    }

    /// <summary>Waits until the worker is nudged, or until <paramref name="timeout"/> has passed.</summary>
    /// <param name="timeout">How long to wait at most, or <see langword="null"/> to wait for a nudge however long it takes.</param>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    /// <returns><see langword="true"/> when nudged, <see langword="false"/> when the time ran out first.</returns>
    public Task<bool> WaitAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (timeout is null)
            return _signal.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);

        var wait = timeout.Value;
        if (wait < TimeSpan.Zero)
            wait = TimeSpan.Zero;
        if (wait > LongestWait)
            wait = LongestWait;

        return _signal.WaitAsync(wait, cancellationToken);
    }
}
