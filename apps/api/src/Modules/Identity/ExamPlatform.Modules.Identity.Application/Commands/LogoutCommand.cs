namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Ends the caller's current session (FR-4).</summary>
/// <param name="UserId">The signed-in user (the access token's <c>sub</c> claim).</param>
/// <param name="SessionId">The session to end (the access token's <c>sid</c> claim).</param>
public sealed record LogoutCommand(Guid UserId, Guid SessionId);
