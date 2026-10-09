namespace ExamPlatform.Modules.Identity.Application.Dtos;

/// <summary>Read-only projection of a role, as listed to the administrators who assign roles (FR-2).</summary>
/// <param name="Id">The role's identifier, the value an assign-role request takes.</param>
/// <param name="Name">The role's unique name.</param>
/// <param name="RequiresTwoFactor">Whether signing in as this role requires a second factor (FR-3).</param>
/// <param name="Permissions">The codes of the permissions the role grants, in alphabetical order.</param>
public sealed record RoleDto(
    Guid Id,
    string Name,
    bool RequiresTwoFactor,
    IReadOnlyCollection<string> Permissions);
