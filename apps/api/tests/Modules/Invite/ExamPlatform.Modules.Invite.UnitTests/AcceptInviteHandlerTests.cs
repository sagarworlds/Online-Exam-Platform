using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using NSubstitute;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

public class AcceptInviteHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Email = "candidate@example.com";

    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();
    private readonly IInviteUnitOfWork _unitOfWork = Substitute.For<IInviteUnitOfWork>();
    private readonly AcceptInviteHandler _handler;

    public AcceptInviteHandlerTests() =>
        _handler = new AcceptInviteHandler(_repository, _unitOfWork, new FakeClock(Now));

    [Fact]
    public async Task HandleAsync_LooksTheInviteUpByTheUpperCasedCode_AcceptsAndSaves()
    {
        var invite = new InviteAggregate(Guid.NewGuid(), null, Email, Guid.NewGuid(), Now);
        var code = invite.GenerateCode(72, Now).Code;
        _repository.GetByCodeAsync(code, Arg.Any<CancellationToken>()).Returns(invite);
        var userId = Guid.NewGuid();

        var result = await _handler.HandleAsync(new AcceptInviteCommand($" {code.ToLowerInvariant()} ", userId, Email), CancellationToken.None);

        Assert.Equal(InviteStatus.Accepted, result.Status);
        Assert.Equal(invite.Id, result.Id);
        Assert.Equal(userId, invite.AcceptedByUserId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOSUCHCD")]
    public async Task HandleAsync_WithAnUnknownOrMissingCode_ThrowsInvalidCode_AndSavesNothing(string? code)
    {
        var error = await Assert.ThrowsAsync<InvalidInviteCodeError>(
            () => _handler.HandleAsync(new AcceptInviteCommand(code, Guid.NewGuid(), Email), CancellationToken.None));

        Assert.Equal("invalid_invite_code", error.ErrorCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForADifferentAddress_ThrowsMismatch_AndSavesNothing()
    {
        var invite = new InviteAggregate(Guid.NewGuid(), null, Email, Guid.NewGuid(), Now);
        var code = invite.GenerateCode(72, Now).Code;
        _repository.GetByCodeAsync(code, Arg.Any<CancellationToken>()).Returns(invite);

        await Assert.ThrowsAsync<InviteEmailMismatchError>(
            () => _handler.HandleAsync(new AcceptInviteCommand(code, Guid.NewGuid(), "other@example.com"), CancellationToken.None));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
