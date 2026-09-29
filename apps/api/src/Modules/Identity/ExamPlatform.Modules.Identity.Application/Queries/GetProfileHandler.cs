using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>Handles <see cref="GetProfileQuery"/>.</summary>
public sealed class GetProfileHandler(IUserRepository userRepository)
{
    /// <summary>Loads and projects a user's profile.</summary>
    /// <param name="query">Which user to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UserNotFoundError">No user matches the given id.</exception>
    public async Task<UserProfileDto> HandleAsync(GetProfileQuery query, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(query.UserId, cancellationToken)
            ?? throw new UserNotFoundError();

        return new UserProfileDto(
            user.Id,
            user.Email,
            user.PhoneNumber,
            user.DisplayName,
            user.Status.ToString(),
            user.Roles.Select(r => r.Name).ToList());
    }
}
