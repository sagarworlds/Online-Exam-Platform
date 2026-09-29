namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Grants a role to a user (FR-2 RBAC administration).</summary>
/// <param name="UserId">The user to grant the role to.</param>
/// <param name="RoleId">The role to grant.</param>
/// <param name="ActorUserId">The admin performing this action, for the audit trail.</param>
/// <param name="ActorRole">The acting admin's role name, for the audit trail.</param>
public sealed record AssignRoleCommand(Guid UserId, Guid RoleId, Guid ActorUserId, string ActorRole);
