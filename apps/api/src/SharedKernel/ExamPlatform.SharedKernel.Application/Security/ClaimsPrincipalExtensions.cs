using System.Security.Claims;

namespace ExamPlatform.SharedKernel.Application.Security;

/// <summary>
/// Reads the caller's identity from the claims that <c>JwtTokenGenerator</c> (Identity.Infrastructure)
/// embeds in the access token, so every module takes the actor from the token rather than from the
/// request body (FR-2). It lives in SharedKernel.Application, which every module's Endpoints project
/// already references, so no module needs a reference to another module's Endpoints for it.
/// </summary>
/// <remarks>
/// The claim names are literals instead of <c>JwtRegisteredClaimNames</c> so this project does not
/// take a JWT package dependency for three strings. The Host turns off inbound claim mapping, so
/// the names below are exactly the names the token was written with.
/// </remarks>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The JWT claim holding the authenticated user's id.</summary>
    public const string SubjectClaim = "sub";

    /// <summary>The JWT claim that names the session a token was issued for.</summary>
    public const string SessionIdClaim = "sid";

    /// <summary>The JWT claim that carries one permission code (one claim per permission).</summary>
    public const string PermissionClaim = "perm";

    private const string ShortRoleClaim = "role";
    private const string UnknownRole = "Unknown";

    /// <summary>The authenticated user's id, from the "sub" claim.</summary>
    /// <param name="user">The current request's authenticated principal.</param>
    /// <returns>The user id.</returns>
    /// <exception cref="MissingAuthenticatedUserError">The claim is missing, is not a GUID, or is the empty GUID.</exception>
    public static Guid GetUserId(this ClaimsPrincipal user) => ReadGuidClaim(user, SubjectClaim);

    /// <summary>
    /// The id of the session the caller's token was issued for, from the "sid" claim. Every
    /// authenticated request has one: a token without it is refused before any endpoint runs.
    /// </summary>
    /// <param name="user">The current request's authenticated principal.</param>
    /// <returns>The session id.</returns>
    /// <exception cref="MissingAuthenticatedUserError">The claim is missing, is not a GUID, or is the empty GUID.</exception>
    public static Guid GetSessionId(this ClaimsPrincipal user) => ReadGuidClaim(user, SessionIdClaim);

    /// <summary>The authenticated user's primary role name, for audit-trail attribution.</summary>
    /// <remarks>
    /// <c>JwtTokenGenerator</c> builds <c>new JwtSecurityToken(claims: ...)</c>, which applies no
    /// outbound claim-type mapping, so the long <see cref="ClaimTypes.Role"/> URI survives into the
    /// token and (with inbound mapping off) back out of it. The short <c>role</c> name is read as a
    /// fallback so this stays correct if tokens are ever issued through a
    /// <c>SecurityTokenDescriptor</c>, which would write the short name.
    /// </remarks>
    /// <param name="user">The current request's authenticated principal.</param>
    /// <returns>The first role claim's value, or <c>Unknown</c> when the token carries none.</returns>
    public static string GetPrimaryRole(this ClaimsPrincipal user)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrWhiteSpace(role))
        {
            role = user.FindFirst(ShortRoleClaim)?.Value;
        }

        return string.IsNullOrWhiteSpace(role) ? UnknownRole : role;
    }

    /// <summary>Whether the caller's token carries <paramref name="permissionCode"/> as a "perm" claim.</summary>
    /// <remarks>
    /// Permissions are baked into the token at sign-in, so this needs no database round trip; a
    /// permission granted or revoked later takes effect once the holder signs in again.
    /// </remarks>
    /// <param name="user">The current request's authenticated principal.</param>
    /// <param name="permissionCode">The permission to look for, e.g. <c>batch.manage</c>. Matched exactly (case-sensitive).</param>
    /// <returns><see langword="true"/> when a matching claim is present; <see langword="false"/> otherwise, always for a blank code (no permission has one, so it fails closed).</returns>
    public static bool HasPermission(this ClaimsPrincipal user, string? permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode)
        && user.Claims.Any(c => c.Type == PermissionClaim && c.Value == permissionCode);

    private static Guid ReadGuidClaim(ClaimsPrincipal user, string claimType)
    {
        // The empty GUID is rejected as well: it would pass TryParse but names nobody.
        if (Guid.TryParse(user.FindFirst(claimType)?.Value, out var value) && value != Guid.Empty)
        {
            return value;
        }

        throw new MissingAuthenticatedUserError(claimType);
    }
}
