using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="UpdateProfileCommand"/>.</summary>
public sealed class UpdateProfileHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
{
    /// <summary>Applies the profile change.</summary>
    /// <param name="command">The user and new display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UserNotFoundError">No user matches the given id.</exception>
    public async Task HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken)
            ?? throw new UserNotFoundError();

        user.UpdateDisplayName(command.DisplayName);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
