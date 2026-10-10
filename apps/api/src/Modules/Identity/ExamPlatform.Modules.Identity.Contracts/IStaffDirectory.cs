namespace ExamPlatform.Modules.Identity.Contracts;

/// <summary>
/// What other modules may ask Identity about the people who run the platform (ADR 0001): who to tell when something needs a staff
/// member's attention. Consumed through this Contracts project only, never through Identity's Domain, Application or Infrastructure.
/// </summary>
public interface IStaffDirectory
{
    /// <summary>The e-mail addresses of the active users who hold a permission, through any of their roles.</summary>
    /// <remarks>
    /// Only people who can sign in are included: a suspended user, or one who never gave an e-mail address, is left out. Each address
    /// appears once however many roles grant the permission.
    /// </remarks>
    /// <param name="permissionCode">The permission, such as <c>exam.manage</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The addresses, in a stable order; empty when nobody holds the permission.</returns>
    Task<IReadOnlyList<string>> GetEmailsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken);

    /// <summary>
    /// The active users who hold a permission, each with the address to e-mail them, so a caller can tell them by e-mail and by their in-app
    /// feed (FR-39) from one lookup.
    /// </summary>
    /// <remarks>
    /// The same rules as <see cref="GetEmailsWithPermissionAsync"/>: only people who can sign in, and one entry per user however many roles
    /// grant the permission. A user with no e-mail address is included with an empty address, since the feed still reaches them.
    /// </remarks>
    /// <param name="permissionCode">The permission, such as <c>exam.manage</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recipients, in a stable order; empty when nobody holds the permission.</returns>
    Task<IReadOnlyList<StaffRecipient>> GetActiveRecipientsWithPermissionAsync(string permissionCode, CancellationToken cancellationToken);
}

/// <summary>A staff member who can be told something, by their account and their address.</summary>
/// <param name="UserId">The account, which is where the in-app feed is kept.</param>
/// <param name="Email">The address to e-mail; empty when the account has none.</param>
public sealed record StaffRecipient(Guid UserId, string Email);
