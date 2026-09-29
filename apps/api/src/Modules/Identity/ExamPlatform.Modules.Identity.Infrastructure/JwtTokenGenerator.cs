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
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);

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

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
