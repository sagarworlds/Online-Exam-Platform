namespace ExamPlatform.Modules.Identity.Contracts;

/// <summary>
/// Which of some accounts hold a phone number that matches what staff typed (ADR 0001), so a page that lists the candidates of an exam can be
/// searched by number without being given the numbers. Identity holds the numbers, as they were entered, and owns the rule for matching one,
/// so other modules ask this question and never read a number. Consumed through this Contracts project only.
/// </summary>
public interface IAccountPhoneSearch
{
    /// <summary>The accounts, among the given ones, whose phone number holds the digits that were typed.</summary>
    /// <remarks>
    /// Only the digits are compared: spaces, dashes, dots and brackets, a trunk <c>0</c> and a country code in front of the number do not
    /// stop a match, and the typed part may be any stretch of the number. Text that is not a phone number, or holds fewer than three digits,
    /// matches nothing. An account is returned only when it is one of the given ones, so the answer cannot be used to look through
    /// accounts the caller was not asking about. The numbers themselves are never returned.
    /// </remarks>
    /// <param name="accountIds">The accounts to look through.</param>
    /// <param name="phone">What was typed, as it was typed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids of the accounts whose number matches; empty when none does.</returns>
    Task<IReadOnlySet<Guid>> FindAccountsByPhoneAsync(IReadOnlyCollection<Guid> accountIds, string phone, CancellationToken cancellationToken);
}
