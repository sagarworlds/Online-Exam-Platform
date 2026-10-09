using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Queries;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class ListRolesHandlerTests
{
    [Fact]
    public async Task HandleAsync_ProjectsEveryRoleWithItsPermissionCodesInAlphabeticalOrder()
    {
        var teacher = Role.Create("InstituteTeacher", requiresTwoFactor: false);
        teacher.Grant(Permission.Create("invite.manage", "Manage invites"));
        teacher.Grant(Permission.Create("batch.manage", "Manage batches"));
        var candidate = Role.Create("Candidate", requiresTwoFactor: false);

        var repository = Substitute.For<IRoleRepository>();
        repository.ListAsync(Arg.Any<CancellationToken>()).Returns([candidate, teacher]);

        var result = await new ListRolesHandler(repository).HandleAsync(CancellationToken.None);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal(candidate.Id, first.Id);
                Assert.Equal("Candidate", first.Name);
                Assert.False(first.RequiresTwoFactor);
                Assert.Empty(first.Permissions);
            },
            second =>
            {
                Assert.Equal(teacher.Id, second.Id);
                Assert.Equal(["batch.manage", "invite.manage"], second.Permissions);
            });
    }

    [Fact]
    public async Task HandleAsync_WithNoRoles_ReturnsAnEmptyList()
    {
        var repository = Substitute.For<IRoleRepository>();
        repository.ListAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListRolesHandler(repository).HandleAsync(CancellationToken.None);

        Assert.Empty(result);
    }
}
