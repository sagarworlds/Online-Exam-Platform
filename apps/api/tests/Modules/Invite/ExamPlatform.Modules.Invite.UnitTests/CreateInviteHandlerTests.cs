using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using NSubstitute;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

public class CreateInviteHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesInviteAndReturnsDto()
    {
        var repository = Substitute.For<IInviteRepository>();
        var unitOfWork = Substitute.For<IInviteUnitOfWork>();
        var handler = new CreateInviteHandler(repository, unitOfWork);

        var examId = Guid.NewGuid();
        var batchMemberId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var email = "candidate@example.com";
        var command = new CreateInviteCommand(examId, batchMemberId, email, createdBy);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(email, result.Email);
        Assert.Equal(examId, result.ExamId);
        Assert.Equal(batchMemberId, result.BatchMemberId);

        repository.Received(1).Add(Arg.Is<InviteAggregate>(i =>
            i.Email == email &&
            i.ExamId == examId &&
            i.BatchMemberId == batchMemberId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithInvalidEmail_ThrowsException()
    {
        var handler = new CreateInviteHandler(
            Substitute.For<IInviteRepository>(),
            Substitute.For<IInviteUnitOfWork>());

        var command = new CreateInviteCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "not-an-email",
            Guid.NewGuid());

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(command, CancellationToken.None));
    }
}
