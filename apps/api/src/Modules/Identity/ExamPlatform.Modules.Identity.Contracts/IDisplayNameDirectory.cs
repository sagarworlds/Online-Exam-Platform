namespace ExamPlatform.Modules.Identity.Contracts;

/// <summary>
/// What other modules may ask Identity about how an account is named (ADR 0001), so a leaderboard can show who a rank belongs to without
/// reading Identity's tables. Consumed through this Contracts project only.
/// </summary>
public interface IDisplayNameDirectory
{
    /// <summary>The display name of each of the given accounts that exists.</summary>
    /// <param name="userIds">The accounts to name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The names by account id; an account that does not exist is absent from the result.</returns>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
