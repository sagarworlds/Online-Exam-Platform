using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Idempotently seeds the roles and permissions every environment needs to
/// exist before any real usage — RBAC has nothing to check against otherwise.
/// What gets seeded is defined by <see cref="RbacCatalog"/>. Runs from the Host's
/// migrate-and-seed step, and is safe to run any number of times.
/// </summary>
public static class IdentitySeeder
{
    /// <summary>
    /// Makes the database hold every role, permission and grant in <see cref="RbacCatalog"/>,
    /// adding whatever is missing and leaving everything else as it is.
    /// </summary>
    /// <remarks>
    /// This is an upsert, not a "seed once" step: a database seeded by an earlier release
    /// (three permissions, grants only for SuperAdmin) picks up new permissions and grants the
    /// next time this runs, which an early return once any role exists would never allow.
    /// It is additive only. It never revokes a grant or changes <c>RequiresTwoFactor</c> on a
    /// role that already exists, because taking a capability away should be a deliberate,
    /// audited admin action and not a side effect of a deployment. Permissions are baked into a
    /// token when it is issued, so users pick up new grants the next time they sign in.
    /// Run it from one process at a time (see ADR 0002): two concurrent runs can both try to
    /// insert the same role or permission and one of them fails on the unique index.
    /// </remarks>
    /// <param name="context">The Identity module's database context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SeedAsync(IdentityDbContext context, CancellationToken cancellationToken)
    {
        var permissions = await context.Permissions.ToDictionaryAsync(p => p.Code, cancellationToken);
        foreach (var definition in RbacCatalog.Permissions)
        {
            if (!permissions.ContainsKey(definition.Code))
            {
                var permission = Permission.Create(definition.Code, definition.Description);
                context.Permissions.Add(permission);
                permissions[definition.Code] = permission;
            }
        }

        // Loaded with their permissions so Grant sees the grants a role already has and does
        // not add a duplicate row to the join table.
        var roles = await context.Roles
            .Include(r => r.Permissions)
            .ToDictionaryAsync(r => r.Name, cancellationToken);
        foreach (var definition in RbacCatalog.Roles)
        {
            if (!roles.TryGetValue(definition.Name, out var role))
            {
                role = Role.Create(definition.Name, definition.RequiresTwoFactor);
                context.Roles.Add(role);
                roles[definition.Name] = role;
            }

            foreach (var code in definition.PermissionCodes)
            {
                role.Grant(permissions[code]);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
