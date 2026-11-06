namespace ExamPlatform.Modules.Identity.Contracts;

/// <summary>
/// Signing a student in because they opened a link that was sent to their e-mail address, with no one-time code: the link is the proof
/// that they can read that mailbox. Identity owns accounts and sessions, so the Invite module, which owns the link, asks for the account
/// and the session through this contract and never touches either. Consumed through this Contracts project only.
/// </summary>
/// <remarks>
/// The two steps are separate so the caller can consume its single-use code between them: the account is settled first, so a refused
/// account (locked, or not a student) never burns the code, and the session is started last, so a failure there leaves the code
/// spent and the student able to sign in the ordinary way.
/// </remarks>
public interface IDirectSignIn
{
    /// <summary>
    /// Finds the student account that holds an e-mail address, comparing without regard to case, or makes one when nobody holds it. A new
    /// account is active at once (the link proved the address), holds the Candidate role, is named after the part of the address before
    /// the <c>@</c>, and has a placeholder adult date of birth, which staff can correct.
    /// </summary>
    /// <param name="email">The invited e-mail address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The account's id, and whether this call made it.</returns>
    /// <exception cref="ExamPlatform.SharedKernel.Domain.DomainException">
    /// The account is suspended or deactivated (<c>account_locked</c>), or it is not a student account, or more than one account holds
    /// the address (<c>direct_sign_in_not_allowed</c>). Staff accounts are never signed in this way: they need a password and a code.
    /// </exception>
    Task<DirectSignInAccount> FindOrCreateStudentAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a session for an account <see cref="FindOrCreateStudentAsync"/> returned. It checks the account again, makes a pending
    /// account active, and ends any other session the account has, as every sign-in does.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="deviceFingerprint">The client's device signature, if it sent one.</param>
    /// <param name="ipAddress">The client's IP address, if it could be read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token and the id of the session it belongs to.</returns>
    /// <exception cref="ExamPlatform.SharedKernel.Domain.DomainException">The account can no longer be signed in this way.</exception>
    Task<DirectSignInSession> StartSessionAsync(Guid userId, string? deviceFingerprint, string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>The account a direct sign-in is for.</summary>
/// <param name="UserId">The account's id.</param>
/// <param name="Created">Whether the sign-in made the account, because nobody held the address.</param>
public sealed record DirectSignInAccount(Guid UserId, bool Created);

/// <summary>The session a direct sign-in started.</summary>
/// <param name="AccessToken">The signed token the client sends from now on.</param>
/// <param name="SessionId">The session the token belongs to.</param>
public sealed record DirectSignInSession(string AccessToken, Guid SessionId);
