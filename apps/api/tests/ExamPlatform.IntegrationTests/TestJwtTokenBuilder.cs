using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Hand-signs JWTs that have no Identity session behind them: no <c>sid</c> claim,
/// no persisted user, and role names that are not seeded. Kept only for negative
/// tests that need such a token (e.g. proving a sid-less token is rejected); every
/// test that needs an authenticated caller signs in with
/// <see cref="TestSessions.SignInAsAsync"/> instead. Never add <c>perm</c> claims
/// here — permission tests must get their claims from the real seeded roles, or
/// they stop proving that the seeded RBAC data actually grants access.
/// </summary>
public static class TestJwtTokenBuilder
{
    private static readonly string SigningKey = "integration-test-only-signing-key-at-least-32-bytes";
    private static readonly string Issuer = "exam-platform-tests";
    private static readonly string Audience = "exam-platform-tests-clients";

    /// <summary>Generates a valid JWT token for a test user with admin role.</summary>
    public static string GenerateAdminToken(Guid? userId = null)
    {
        return GenerateToken(userId ?? Guid.NewGuid(), "admin", ["Admin"]);
    }

    /// <summary>Generates a valid JWT token for a test user with candidate role.</summary>
    public static string GenerateCandidateToken(Guid? userId = null)
    {
        return GenerateToken(userId ?? Guid.NewGuid(), "candidate", ["Candidate"]);
    }

    /// <summary>Generates a valid JWT token for a test user with guardian role.</summary>
    public static string GenerateGuardianToken(Guid? userId = null)
    {
        return GenerateToken(userId ?? Guid.NewGuid(), "guardian", ["Guardian"]);
    }

    private static string GenerateToken(Guid userId, string username, string[] roles)
    {
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
