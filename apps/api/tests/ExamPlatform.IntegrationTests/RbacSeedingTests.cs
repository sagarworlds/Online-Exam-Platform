using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Proves the RBAC reference data (FR-2) reaches a real database and stays correct however the
/// seeder is run: on a fresh database, on one seeded by an earlier release, and repeatedly.
/// The factory's own startup already ran the seeder once (it migrates and seeds in Development).
/// </summary>
public sealed class RbacSeedingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // What the first release's seeder wrote: three permissions, granted to SuperAdmin only.
    private static readonly string[] OriginalPermissionCodes =
        [RbacCatalog.PermissionCodes.AuditRead, RbacCatalog.PermissionCodes.ConsentManage, RbacCatalog.PermissionCodes.RoleAssign];

    private static int ExpectedGrantCount => RbacCatalog.Roles.Sum(r => r.PermissionCodes.Count);

    [Fact]
    public async Task Startup_SeedsEveryCatalogRolePermissionAndGrant()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var permissionCodes = await db.Permissions.Select(p => p.Code).ToListAsync();
        var roles = await db.Roles.Include(r => r.Permissions).ToListAsync();

        Assert.Equal(
            RbacCatalog.Permissions.Select(p => p.Code).Order(StringComparer.Ordinal),
            permissionCodes.Order(StringComparer.Ordinal));
        Assert.Equal(
            RbacCatalog.Roles.Select(r => r.Name).Order(StringComparer.Ordinal),
            roles.Select(r => r.Name).Order(StringComparer.Ordinal));

        foreach (var definition in RbacCatalog.Roles)
        {
            var role = roles.Single(r => r.Name == definition.Name);
            Assert.Equal(definition.RequiresTwoFactor, role.RequiresTwoFactor);
            Assert.Equal(
                definition.PermissionCodes.Order(StringComparer.Ordinal),
                role.Permissions.Select(p => p.Code).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task SeedAsync_OnDatabaseSeededByOldSeeder_AddsMissingPermissionsAndGrants()
    {
        // Put the database back into the state the first release's seeder left it in: only the
        // original three permissions, and no grant for any other.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM identity."RolePermissions"
                WHERE "PermissionsId" IN (
                    SELECT "Id" FROM identity."Permissions"
                    WHERE "Code" NOT IN ('admin.audit.read', 'consent.manage', 'identity.role.assign'));
                DELETE FROM identity."Permissions"
                WHERE "Code" NOT IN ('admin.audit.read', 'consent.manage', 'identity.role.assign');
                """);
            Assert.Equal(OriginalPermissionCodes.Length, await db.Permissions.CountAsync());
        }

        Guid originalAuditReadId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            originalAuditReadId = (await db.Permissions.SingleAsync(p => p.Code == RbacCatalog.PermissionCodes.AuditRead)).Id;

            await IdentitySeeder.SeedAsync(db, CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var examAdmin = await db.Roles.Include(r => r.Permissions)
                .SingleAsync(r => r.Name == RbacCatalog.RoleNames.ExamAdmin);

            Assert.True(examAdmin.HasPermission(RbacCatalog.PermissionCodes.ExamManage));
            Assert.Equal(RbacCatalog.Permissions.Count, await db.Permissions.CountAsync());
            Assert.Equal(ExpectedGrantCount, await CountGrantsAsync(db));

            // The permissions that already existed were kept, not deleted and recreated.
            Assert.Equal(
                originalAuditReadId,
                (await db.Permissions.SingleAsync(p => p.Code == RbacCatalog.PermissionCodes.AuditRead)).Id);
        }
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicateRolesPermissionsOrGrants()
    {
        for (var run = 0; run < 2; run++)
        {
            using var scope = factory.Services.CreateScope();
            await IdentitySeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<IdentityDbContext>(), CancellationToken.None);
        }

        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(RbacCatalog.Roles.Count, await db.Roles.CountAsync());
        Assert.Equal(RbacCatalog.Permissions.Count, await db.Permissions.CountAsync());
        Assert.Equal(ExpectedGrantCount, await CountGrantsAsync(db));
    }

    [Fact]
    public async Task SeedAsync_OnExistingRole_NeverRevokesAGrantOrChangesTwoFactor()
    {
        // An operator has, on purpose, given Candidate a permission the catalog does not and made
        // it require 2FA. A deployment must not silently undo either: removing a capability is a
        // deliberate admin action, not a side effect of startup.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var candidate = await db.Roles.Include(r => r.Permissions)
                .SingleAsync(r => r.Name == RbacCatalog.RoleNames.Candidate);
            candidate.Grant(await db.Permissions.SingleAsync(p => p.Code == RbacCatalog.PermissionCodes.BatchRead));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE identity."Roles" SET "RequiresTwoFactor" = true WHERE "Name" = 'Candidate'""");
        }

        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                await IdentitySeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<IdentityDbContext>(), CancellationToken.None);
            }

            using var check = factory.Services.CreateScope();
            var db = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var candidate = await db.Roles.Include(r => r.Permissions)
                .SingleAsync(r => r.Name == RbacCatalog.RoleNames.Candidate);
            Assert.True(candidate.HasPermission(RbacCatalog.PermissionCodes.BatchRead));
            Assert.True(candidate.RequiresTwoFactor);
        }
        finally
        {
            // Leave the shared database as the catalog defines it for the other tests.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM identity."RolePermissions"
                WHERE "RoleId" = (SELECT "Id" FROM identity."Roles" WHERE "Name" = 'Candidate');
                UPDATE identity."Roles" SET "RequiresTwoFactor" = false WHERE "Name" = 'Candidate';
                """);
        }
    }

    [Fact]
    public async Task TheMigrationsGrants_GiveQuestionReadAndReviewToTheRightRoles_OnADatabaseThatHadNeitherPermission()
    {
        // The state a database was in before FR-8: the two permissions do not exist, so no role holds them.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM identity."RolePermissions"
                WHERE "PermissionsId" IN (SELECT "Id" FROM identity."Permissions" WHERE "Code" IN ('question.read', 'question.review'));
                DELETE FROM identity."Permissions" WHERE "Code" IN ('question.read', 'question.review');
                """);

            // Run twice: a migration that is repeated, or runs after the seeder, must change nothing the second time.
            await db.Database.ExecuteSqlRawAsync(QuestionReviewPermissions.GrantSql);
            var grants = await CountGrantsAsync(db);
            await db.Database.ExecuteSqlRawAsync(QuestionReviewPermissions.GrantSql);
            Assert.Equal(grants, await CountGrantsAsync(db));
        }

        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roles = await context.Roles.Include(r => r.Permissions).ToListAsync();
        // Whatever the catalog says each role holds, the migration alone has to arrive at for these two permissions.
        foreach (var definition in RbacCatalog.Roles)
        {
            var held = roles.Single(r => r.Name == definition.Name).Permissions.Select(p => p.Code).ToHashSet();
            foreach (var code in new[] { RbacCatalog.PermissionCodes.QuestionRead, RbacCatalog.PermissionCodes.QuestionReview })
            {
                Assert.True(
                    definition.PermissionCodes.Contains(code) == held.Contains(code),
                    $"{definition.Name}: the catalog says {(definition.PermissionCodes.Contains(code) ? "grant" : "do not grant")} {code}, the migration did the opposite.");
            }
        }
    }

    private static Task<int> CountGrantsAsync(IdentityDbContext db) =>
        db.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM identity."RolePermissions" """).SingleAsync();
}
