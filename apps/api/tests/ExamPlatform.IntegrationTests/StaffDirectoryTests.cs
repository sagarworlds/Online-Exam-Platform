using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>Asking Identity who holds a permission, against a real database with the seeded roles.</summary>
public sealed class StaffDirectoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<IReadOnlyList<string>> HoldersAsync(string permission)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffDirectory>().GetEmailsWithPermissionAsync(permission, CancellationToken.None);
    }

    [Fact]
    public async Task SomeoneWhoseRoleGrantsThePermission_IsListed_AndSomeoneWhoseRoleDoesNotIsNot()
    {
        var examAdmin = await factory.SignInAsAsync(RbacCatalog.RoleNames.ExamAdmin);
        var author = await factory.SignInAsAsync(RbacCatalog.RoleNames.ContentAuthor);

        var holders = await HoldersAsync(RbacCatalog.PermissionCodes.ExamManage);

        Assert.Contains(examAdmin.Email, holders);
        Assert.DoesNotContain(author.Email, holders);
    }

    [Fact]
    public async Task EachAddressAppearsOnce_EvenForSomeoneWithSeveralRoles()
    {
        await factory.SignInAsAsync(RbacCatalog.RoleNames.ExamAdmin);
        await factory.SignInAsAsync(RbacCatalog.RoleNames.SuperAdmin);

        var holders = await HoldersAsync(RbacCatalog.PermissionCodes.ExamManage);

        Assert.Equal(holders.Distinct(), holders);
    }

    [Fact]
    public async Task APermissionNobodyHolds_ListsNoOne() => Assert.Empty(await HoldersAsync("no.such.permission"));

    [Fact]
    public async Task ABlankPermission_IsAMistakeInTheCaller_NotAnEmptyAnswer()
    {
        using var scope = factory.Services.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IStaffDirectory>();

        await Assert.ThrowsAsync<ArgumentException>(() => directory.GetEmailsWithPermissionAsync("  ", CancellationToken.None));
    }
}
