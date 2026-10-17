namespace ExamPlatform.Modules.Invite.Contracts;

/// <summary>
/// Moves a person's invitations to a new e-mail address (ADR 0001). Identity asks it when staff change the address on a student's
/// account, so the student's invitations follow them: an invitation can be accepted only by the account that holds the invited
/// address, and a pending one left behind at the old address could never be accepted again. Consumed through this Contracts project only.
/// </summary>
public interface IInviteAddressChanger
{
    /// <summary>
    /// Changes the address on every invitation sent to <paramref name="currentEmail"/>, in every status, so the person's invitations stay
    /// together. Addresses are compared without regard to case.
    /// </summary>
    /// <remarks>
    /// Safe to repeat: when no invitation was sent to the current address (including when it is blank or not a readable address), or the two
    /// addresses are the same, nothing changes and the answer is zero. That is what lets a request that failed part way be sent again.
    /// Only the new address is checked: it must be a valid e-mail address.
    /// </remarks>
    /// <param name="currentEmail">The address the invitations were sent to.</param>
    /// <param name="newEmail">The address they are to have.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many invitations now carry the new address.</returns>
    Task<int> ChangeAddressAsync(string currentEmail, string newEmail, CancellationToken cancellationToken);
}
