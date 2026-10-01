using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Sessions;

/// <summary>
/// A read-only view of one <see cref="UserSession"/> and its owner's account status: just
/// enough for <see cref="SessionValidator"/> to judge a token without loading the whole
/// <see cref="User"/> aggregate on every request.
/// </summary>
/// <param name="UserId">The user the session belongs to.</param>
/// <param name="ExpiresAtUtc">When the session naturally expires.</param>
/// <param name="RevokedAtUtc">When the session was revoked, if it has been.</param>
/// <param name="RevokedReason">Why the session was revoked, if it has been.</param>
/// <param name="UserStatus">The owning account's current status.</param>
public sealed record SessionSnapshot(
    Guid UserId,
    DateTime ExpiresAtUtc,
    DateTime? RevokedAtUtc,
    SessionRevocationReason? RevokedReason,
    UserStatus UserStatus);
