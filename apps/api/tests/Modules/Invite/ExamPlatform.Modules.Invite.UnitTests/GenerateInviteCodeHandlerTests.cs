using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>Staff asking an invite for a code to hand over by hand: always a fresh code, only for an invite that can still use it.</summary>
public class GenerateInviteCodeHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private const string Email = "candidate@example.com";

    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();
    private readonly IInviteUnitOfWork _unitOfWork = Substitute.For<IInviteUnitOfWork>();
    private readonly IInviteLinkBuilder _links = Substitute.For<IInviteLinkBuilder>();
    private readonly InviteAggregate _invite = new(Guid.NewGuid(), null, Email, Guid.NewGuid(), Now);
    private readonly GenerateInviteCodeHandler _handler;

    public GenerateInviteCodeHandlerTests()
    {
        _invite.GenerateCode(72, Now); // the code the invitation itself went out with
        _repository.GetByIdOrThrowAsync(_invite.Id, Arg.Any<CancellationToken>()).Returns(_invite);
        _links.Build(Arg.Any<string>()).Returns(call => "https://app.example/invite?code=" + call.Arg<string>());
        _handler = new GenerateInviteCodeHandler(_repository, _unitOfWork, _links, new FakeClock(Now));
    }

    [Fact]
    public async Task HandleAsync_ReturnsANewCodeAndTheLinkThatCarriesIt_AfterSavingIt()
    {
        var result = await _handler.HandleAsync(new GenerateInviteCodeCommand(_invite.Id, 24), CancellationToken.None);

        Assert.Matches("^[A-Z0-9]{8}$", result.Code);
        Assert.Equal("https://app.example/invite?code=" + result.Code, result.Link);
        Assert.Equal(Now.AddHours(24), result.ExpiresAt);
        Assert.Null(result.UsedAt);
        Assert.Null(result.RevokedAt);
        Assert.Contains(_invite.Codes, c => c.Id == result.Id && c.Code == result.Code);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_EachRequestMakesAnotherCode_AndLeavesTheEarlierOnesAlone()
    {
        var first = _invite.Codes.Single().Code;

        var second = await _handler.HandleAsync(new GenerateInviteCodeCommand(_invite.Id), CancellationToken.None);
        var third = await _handler.HandleAsync(new GenerateInviteCodeCommand(_invite.Id), CancellationToken.None);

        Assert.Equal(3, _invite.Codes.Select(c => c.Code).Distinct().Count());
        Assert.NotEqual(second.Code, third.Code);
        Assert.All(_invite.Codes, c => Assert.True(c.IsValid(Now)));
        Assert.Contains(_invite.Codes, c => c.Code == first);
    }

    [Fact]
    public async Task HandleAsync_UsesTheDefaultLifetimeWhenNoneIsGiven()
    {
        var result = await _handler.HandleAsync(new GenerateInviteCodeCommand(_invite.Id), CancellationToken.None);

        Assert.Equal(Now.AddHours(72), result.ExpiresAt);
    }

    [Fact]
    public async Task HandleAsync_ForAnUnknownInvite_Throws404_AndSavesNothing()
    {
        var unknown = Guid.NewGuid();
        _repository.GetByIdOrThrowAsync(unknown, Arg.Any<CancellationToken>()).ThrowsAsync(new InviteNotFoundError(unknown));

        var error = await Assert.ThrowsAsync<InviteNotFoundError>(
            () => _handler.HandleAsync(new GenerateInviteCodeCommand(unknown), CancellationToken.None));

        Assert.Equal(404, error.HttpStatusCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(InviteStatus.Accepted)]
    [InlineData(InviteStatus.Declined)]
    [InlineData(InviteStatus.Revoked)]
    public async Task HandleAsync_ForAnInviteNoLongerPending_Throws409_AndSavesNothing(InviteStatus status)
    {
        switch (status)
        {
            case InviteStatus.Accepted:
                _invite.Accept(_invite.Codes.Single().Code, Guid.NewGuid(), Email, Now);
                break;
            case InviteStatus.Declined:
                _invite.Decline(Now);
                break;
            default:
                _invite.Revoke(Now);
                break;
        }

        var error = await Assert.ThrowsAsync<InviteStateError>(
            () => _handler.HandleAsync(new GenerateInviteCodeCommand(_invite.Id), CancellationToken.None));

        Assert.Equal(409, error.HttpStatusCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Single(_invite.Codes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public async Task HandleAsync_WithALifetimeOutOfRange_Throws400_AndSavesNothing(int hours)
    {
        var error = await Assert.ThrowsAsync<InvalidInviteExpiryError>(
            () => _handler.HandleAsync(new GenerateInviteCodeCommand(_invite.Id, hours), CancellationToken.None));

        Assert.Equal(400, error.HttpStatusCode);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
