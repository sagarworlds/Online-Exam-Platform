using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Ports;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class StaffDirectoryTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private StaffDirectory Directory => new(_users);

    [Fact]
    public async Task ItReturnsWhatTheRepositoryFoundForThePermission()
    {
        _users.ListActiveEmailsWithPermissionAsync("exam.manage", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["a@example.com", "b@example.com"]));

        var emails = await Directory.GetEmailsWithPermissionAsync("exam.manage", CancellationToken.None);

        Assert.Equal(["a@example.com", "b@example.com"], emails);
    }

    [Fact]
    public async Task SpacesAroundThePermissionCodeAreIgnored()
    {
        await Directory.GetEmailsWithPermissionAsync("  exam.manage ", CancellationToken.None);

        await _users.Received(1).ListActiveEmailsWithPermissionAsync("exam.manage", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankPermissionCodeIsRefusedBeforeAnythingIsQueried(string code)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.GetEmailsWithPermissionAsync(code, CancellationToken.None));

        await _users.DidNotReceive().ListActiveEmailsWithPermissionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
