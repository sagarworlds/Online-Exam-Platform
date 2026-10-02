using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Ports;
using NSubstitute;

namespace ExamPlatform.Modules.Invite.UnitTests;

public class InviteExamDeletionGuardTests
{
    private readonly IInviteRepository repository = Substitute.For<IInviteRepository>();
    private readonly Guid examId = Guid.NewGuid();

    [Fact]
    public async Task WhenSomeoneHasBeenInvited_ItObjects_AndSaysWhatToDoFirst()
    {
        repository.AnyLiveForExamAsync(examId, Arg.Any<CancellationToken>()).Returns(true);

        var objections = await new InviteExamDeletionGuard(repository).FindObjectionsAsync(examId, CancellationToken.None);

        var reason = Assert.Single(objections);
        Assert.Contains("invited", reason);
        Assert.Contains("revoke", reason);
    }

    [Fact]
    public async Task WhenNoLiveInvitationExists_ItHasNoObjection()
    {
        repository.AnyLiveForExamAsync(examId, Arg.Any<CancellationToken>()).Returns(false);

        Assert.Empty(await new InviteExamDeletionGuard(repository).FindObjectionsAsync(examId, CancellationToken.None));
    }
}
