using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Issues signed JWT access tokens. Permission codes are baked into the token as
/// claims at login time (see ADR 0001's RBAC design), so authorization checks
/// never need a database round trip — the accepted trade-off is that revoking a
/// permission takes effect only once the holder's token is next reissued.
/// </summary>
public sealed class JwtTokenGenerator(IConfiguration configuration) : ITokenGenerator
{
    /// <inheritdoc />
    public string GenerateAccessToken(User user, UserSession session)
    {
        var signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Configuration value 'Jwt:SigningKey' is required to issue tokens.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("sid", session.Id.ToString()),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
        };

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role.Name)));
        claims.AddRange(user.Roles
            .SelectMany(role => role.Permissions)
            .Select(permission => permission.Code)
            .Distinct()
            .Select(code => new Claim("perm", code)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);

        // The token's lifetime is the session's, so a token can never outlive its session and
        // the sign-in lifetime has one source of truth (LoginSessionIssuer), set from the
        // injected Clock rather than read from the system clock here.
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            notBefore: session.IssuedAtUtc,
            expires: session.ExpiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
