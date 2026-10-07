namespace ExamPlatform.Modules.Identity.Contracts;

/// <summary>
/// What other modules may ask Identity about how to reach a person who already has an account (ADR 0001): the invitation of an
/// address to an exam wants to tell the account holder on their phone as well. Consumed through this Contracts project only,
/// never through Identity's Domain, Application or Infrastructure.
/// </summary>
public interface IContactDirectory
{
    /// <summary>The phone number registered on the active account that holds an e-mail address.</summary>
    /// <remarks>
    /// Only an account that can sign in counts: one still waiting to be verified, or suspended, has no number to give. The number is
    /// returned as it was entered at registration, so it may still need a country code before it can be dialled.
    /// </remarks>
    /// <param name="email">The address the account was registered with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number, or null when no active account holds the address or the account has no phone number.</returns>
    Task<string?> FindPhoneNumberByEmailAsync(string email, CancellationToken cancellationToken);
}
