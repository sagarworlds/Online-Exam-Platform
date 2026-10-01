using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Domain.Rbac;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class RbacCatalogTests
{
    private static RoleDefinition Role(string name) =>
        RbacCatalog.Roles.Single(r => r.Name == name);

    [Fact]
    public void SuperAdmin_HoldsEveryDefinedPermission()
    {
        var granted = Role(RbacCatalog.RoleNames.SuperAdmin).PermissionCodes;

        Assert.Equal(
            RbacCatalog.Permissions.Select(p => p.Code).Order(StringComparer.Ordinal),
            granted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryRoleGrant_ReferencesADefinedPermissionCode()
    {
        var defined = RbacCatalog.Permissions.Select(p => p.Code).ToHashSet();

        foreach (var role in RbacCatalog.Roles)
        {
            Assert.All(role.PermissionCodes, code => Assert.Contains(code, defined));
        }
    }

    [Fact]
    public void PermissionCodes_AndRoleNames_AreUnique()
    {
        Assert.Equal(RbacCatalog.Permissions.Count, RbacCatalog.Permissions.Select(p => p.Code).Distinct().Count());
        Assert.Equal(RbacCatalog.Roles.Count, RbacCatalog.Roles.Select(r => r.Name).Distinct().Count());

        // A repeated code inside one role would be harmless to the seeder but is always a typo.
        foreach (var role in RbacCatalog.Roles)
        {
            Assert.Equal(role.PermissionCodes.Count, role.PermissionCodes.Distinct().Count());
        }
    }

    [Fact]
    public void ExamAdmin_AndInstituteTeacher_Grants_MatchPermissionMatrix()
    {
        Assert.Equal(
            ["batch.manage", "batch.read", "exam.manage", "exam.publish", "guardian.link.manage", "invite.manage"],
            Role(RbacCatalog.RoleNames.ExamAdmin).PermissionCodes.Order(StringComparer.Ordinal));

        Assert.Equal(
            ["batch.manage", "batch.read", "invite.manage"],
            Role(RbacCatalog.RoleNames.InstituteTeacher).PermissionCodes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AdministrationPermissions_AreHeldBySuperAdminOnly()
    {
        string[] administration =
        [
            RbacCatalog.PermissionCodes.AuditRead,
            RbacCatalog.PermissionCodes.ConsentManage,
            RbacCatalog.PermissionCodes.RoleAssign,
        ];

        var holders = RbacCatalog.Roles
            .Where(r => r.PermissionCodes.Intersect(administration).Any())
            .Select(r => r.Name);

        Assert.Equal([RbacCatalog.RoleNames.SuperAdmin], holders);
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.Candidate)]
    [InlineData(RbacCatalog.RoleNames.Guardian)]
    public void Candidate_AndGuardian_HaveNoPermissions(string roleName)
    {
        Assert.Empty(Role(roleName).PermissionCodes);
    }

    [Fact]
    public void Roles_AreTheEightRolesOfRequirementsSection3_WithTheirTwoFactorFlags()
    {
        // FR-3: second factor is mandatory for the admin-facing roles only.
        var expected = new Dictionary<string, bool>
        {
            ["SuperAdmin"] = true,
            ["ExamAdmin"] = true,
            ["ContentAuthor"] = true,
            ["Reviewer"] = true,
            ["Proctor"] = true,
            ["InstituteTeacher"] = false,
            ["Candidate"] = false,
            ["Guardian"] = false,
        };

        Assert.Equal(
            expected.OrderBy(e => e.Key, StringComparer.Ordinal),
            RbacCatalog.Roles.ToDictionary(r => r.Name, r => r.RequiresTwoFactor).OrderBy(e => e.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryNamedPermissionCode_IsADefinedPermission()
    {
        var named = typeof(RbacCatalog.PermissionCodes)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(RbacCatalog.Permissions.Select(p => p.Code).Order(StringComparer.Ordinal), named);
    }

    [Fact]
    public void CandidateRoleName_MatchesTheNameRegistrationAssigns()
    {
        // Registration looks the role up by name; a rename in only one place would make
        // every registration fail with RoleNotFoundError.
        Assert.Equal(RegisterCandidateHandler.CandidateRoleName, RbacCatalog.RoleNames.Candidate);
    }
}
