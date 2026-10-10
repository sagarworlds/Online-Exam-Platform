using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Contracts;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>The <see cref="IDisplayNameDirectory"/> other modules ask how accounts are named.</summary>
public sealed class DisplayNameDirectory(IUserRepository users) : IDisplayNameDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in userIds.Distinct())
        {
            // An account that no longer exists has no name to show, so it is left out and the caller masks it as anonymous.
            var user = await users.GetByIdAsync(id, cancellationToken);
            if (user is not null)
            {
                names[id] = user.DisplayName;
            }
        }

        return names;
    }
}
