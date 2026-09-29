using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class RoleTests
{
    [Fact]
    public void RequiresTwoFactor_ForSuperAdminRole_ReturnsTrue()
    {
        var role = Role.Create("SuperAdmin", requiresTwoFactor: true);

        Assert.True(role.RequiresTwoFactor);
    }

    [Fact]
    public void RequiresTwoFactor_ForCandidateRole_ReturnsFalse()
    {
        var role = Role.Create("Candidate", requiresTwoFactor: false);

        Assert.False(role.RequiresTwoFactor);
    }

    [Fact]
    public void HasPermission_AfterGrant_ReturnsTrueForGrantedCode()
    {
        var role = Role.Create("SuperAdmin", requiresTwoFactor: true);
        var permission = Permission.Create("admin.audit.read", "View the admin audit log");

        role.Grant(permission);

        Assert.True(role.HasPermission("admin.audit.read"));
        Assert.False(role.HasPermission("unrelated.code"));
    }
}
