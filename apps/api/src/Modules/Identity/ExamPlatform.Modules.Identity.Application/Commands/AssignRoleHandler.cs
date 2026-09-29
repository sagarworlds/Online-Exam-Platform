using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Handles <see cref="AssignRoleCommand"/>.</summary>
public sealed class AssignRoleHandler(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IAuditLogger auditLogger,
    IIdentityUnitOfWork unitOfWork)
{
    /// <summary>Grants the role and records the change in the audit trail.</summary>
    /// <param name="command">Who is granting which role to whom.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UserNotFoundError">No user matches <see cref="AssignRoleCommand.UserId"/>.</exception>
    /// <exception cref="RoleNotFoundError">No role matches <see cref="AssignRoleCommand.RoleId"/>.</exception>
    public async Task HandleAsync(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken)
            ?? throw new UserNotFoundError();
        var role = await roleRepository.GetByIdAsync(command.RoleId, cancellationToken)
            ?? throw new RoleNotFoundError();

        user.AssignRole(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.ActorUserId,
                ActorRole: command.ActorRole,
                Action: "Identity.RoleAssigned",
                EntityType: "User",
                EntityId: user.Id.ToString(),
                Metadata: new Dictionary<string, string> { ["roleName"] = role.Name },
                CorrelationId: null),
            cancellationToken);
    }
}
