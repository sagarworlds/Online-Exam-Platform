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
}
