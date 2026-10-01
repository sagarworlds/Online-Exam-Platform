using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>
/// Handles <see cref="LogoutCommand"/>. Revoking the stored session, rather than only
/// discarding the token on the client, is what makes the token stop working: every
/// authenticated request checks its session (FR-4).
/// </summary>
public sealed class LogoutHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Revokes the session as logged out.</summary>
    /// <param name="command">The user and the session to end.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UserNotFoundError">No user matches the given id.</exception>
    /// <exception cref="SessionNotFoundError">The user has no session with the given id.</exception>
    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken)
            ?? throw new UserNotFoundError();

        user.RevokeSession(command.SessionId, clock.UtcNow, SessionRevocationReason.LoggedOut);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
