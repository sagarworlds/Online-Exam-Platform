namespace ExamPlatform.Modules.Notifications.Contracts;

/// <summary>
/// Records an in-app notice in a signed-in account's feed (FR-39). Other modules call it after their own change is saved, so the feed
/// never announces something that did not happen. Consumed through this Contracts project only (ADR 0001).
/// </summary>
public interface IInAppNotifier
{
    /// <summary>
    /// Records the notice unless the same one is already recorded. Recording it again is harmless, so a caller need not check first.
    /// </summary>
    /// <remarks>
    /// It never throws for a store failure: the caller's change is already saved and must not be undone, or reported as failed, because the
    /// feed could not be written. The failure is logged with the notice's details, and the feed simply lacks that one notice.
    /// </remarks>
    /// <param name="notice">What to record, and for whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the account's feed holds the notice, whether it was recorded now or earlier; <see langword="false"/> when
    /// it could not be recorded (the failure is logged).
    /// </returns>
    Task<bool> NotifyAsync(InAppNotice notice, CancellationToken cancellationToken);

    /// <summary>
    /// Records several notices in one check and one save, so a pass that has many people to tell (every candidate of an exam, say) does not
    /// look each one up on its own.
    /// </summary>
    /// <remarks>
    /// The same rules as <see cref="NotifyAsync"/>. Notices already in the feed are left out, and a notice repeated within the batch is recorded
    /// once. If the save fails, none of the batch is recorded and the failure is logged; a pass that runs again records what is still missing.
    /// </remarks>
    /// <param name="notices">What to record, and for whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when every notice in the batch is in the feed afterwards; <see langword="false"/> when some could not be recorded
    /// (the failure is logged).
    /// </returns>
    Task<bool> NotifyManyAsync(IReadOnlyCollection<InAppNotice> notices, CancellationToken cancellationToken);
}
