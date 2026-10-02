using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>
/// Lists every role with the permissions it grants, so an administrator can pick a role to assign
/// without knowing its id in advance (FR-2).
/// </summary>
public sealed class ListRolesHandler(IRoleRepository roleRepository)
{
    /// <summary>Loads and projects every role, ordered by name.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per role, each with its permission codes in alphabetical order.</returns>
    public async Task<IReadOnlyList<RoleDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var roles = await roleRepository.ListAsync(cancellationToken);

        return roles
            .Select(role => new RoleDto(
                role.Id,
                role.Name,
                role.RequiresTwoFactor,
                role.Permissions.Select(permission => permission.Code).Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }
}
