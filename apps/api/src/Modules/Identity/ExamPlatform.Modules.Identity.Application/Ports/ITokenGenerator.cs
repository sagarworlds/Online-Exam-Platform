using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Issues bearer access tokens for an authenticated session.</summary>
public interface ITokenGenerator
{
    /// <summary>
    /// Produces a signed access token embedding the user's id, roles, and
    /// effective permission codes, scoped to one session.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="session">The session this token authorizes.</param>
    string GenerateAccessToken(User user, UserSession session);
}
