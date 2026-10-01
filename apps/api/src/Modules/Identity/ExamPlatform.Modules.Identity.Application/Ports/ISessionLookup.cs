using ExamPlatform.Modules.Identity.Application.Sessions;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>
/// Read-only lookup of the session an access token's <c>sid</c> claim names, for
/// <see cref="SessionValidator"/> to check on every authenticated request (FR-4).
/// </summary>
public interface ISessionLookup
{
    /// <summary>Loads what is needed to judge whether a session is still usable, or null if no such session exists.</summary>
    /// <param name="sessionId">The session's identifier, from the token's <c>sid</c> claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SessionSnapshot?> FindAsync(Guid sessionId, CancellationToken cancellationToken);
}
