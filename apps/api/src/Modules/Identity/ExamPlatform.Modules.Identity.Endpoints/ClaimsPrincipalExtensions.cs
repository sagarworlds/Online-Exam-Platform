using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Reads identity claims embedded by <c>JwtTokenGenerator</c> (see Identity.Infrastructure).</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's id, from the "sub" claim.</summary>
    /// <param name="user">The current request's authenticated principal.</param>
    /// <exception cref="InvalidOperationException">The principal has no "sub" claim.</exception>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("The authenticated principal is missing a 'sub' claim.");
        return Guid.Parse(sub);
    }

    /// <summary>The authenticated user's primary role name, for audit-trail attribution.</summary>
    /// <param name="user">The current request's authenticated principal.</param>
    public static string GetPrimaryRole(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown";
}
