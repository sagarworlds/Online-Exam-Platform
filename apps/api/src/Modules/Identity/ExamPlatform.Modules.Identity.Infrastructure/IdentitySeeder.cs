using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Idempotently seeds the roles and permissions every environment needs to
/// exist before any real usage — RBAC has nothing to check against otherwise.
/// Runs at Host startup; safe to call on every startup since it no-ops once seeded.
/// </summary>
public static class IdentitySeeder
{
    /// <summary>Seeds default roles and permissions if none exist yet.</summary>
    /// <param name="context">The Identity module's database context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SeedAsync(IdentityDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Roles.AnyAsync(cancellationToken))
        {
            return;
        }

        var auditRead = Permission.Create("admin.audit.read", "View the admin audit log");
        var consentManage = Permission.Create("consent.manage", "Record and withdraw consent on behalf of a candidate");
        var identityManage = Permission.Create("identity.role.assign", "Assign roles to users");
        context.Permissions.AddRange(auditRead, consentManage, identityManage);

        // Roles named after exam-platform-requirements.md section 3. RequiresTwoFactor
        // follows FR-3: mandatory for admin-facing roles, not for candidates or guardians.
        var superAdmin = Role.Create("SuperAdmin", requiresTwoFactor: true);
        superAdmin.Grant(auditRead);
        superAdmin.Grant(consentManage);
        superAdmin.Grant(identityManage);

        var examAdmin = Role.Create("ExamAdmin", requiresTwoFactor: true);
        var contentAuthor = Role.Create("ContentAuthor", requiresTwoFactor: true);
        var reviewer = Role.Create("Reviewer", requiresTwoFactor: true);
        var proctor = Role.Create("Proctor", requiresTwoFactor: true);
        var instituteTeacher = Role.Create("InstituteTeacher", requiresTwoFactor: false);
        var candidate = Role.Create(RegisterCandidateHandler.CandidateRoleName, requiresTwoFactor: false);
        var guardian = Role.Create("Guardian", requiresTwoFactor: false);

        context.Roles.AddRange(superAdmin, examAdmin, contentAuthor, reviewer, proctor, instituteTeacher, candidate, guardian);

        await context.SaveChangesAsync(cancellationToken);
    }
}
