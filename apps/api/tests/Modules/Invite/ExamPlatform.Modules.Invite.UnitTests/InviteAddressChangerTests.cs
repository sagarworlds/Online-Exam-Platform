using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>Moving a person's invitations to a new address: all of them, in one save, and nothing else.</summary>
public class InviteAddressChangerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private const string Old = "old@example.com";
    private const string New = "new@example.com";

    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();
    private readonly IInviteUnitOfWork _unitOfWork = Substitute.For<IInviteUnitOfWork>();
    private readonly InviteAddressChanger _changer;

    public InviteAddressChangerTests() =>
        _changer = new InviteAddressChanger(_repository, _unitOfWork, new FakeClock(Now.AddMinutes(5)), NullLogger<InviteAddressChanger>.Instance);

    private static InviteAggregate InviteTo(string email) => new(Guid.NewGuid(), null, email, Guid.NewGuid(), Now);

    private void Holding(params InviteAggregate[] invites) =>
        _repository.ListByEmailAsync(Old, Arg.Any<CancellationToken>()).Returns(invites);

    [Fact]
    public async Task EveryInviteOfTheAddress_MovesToTheNewOne_InOneSave()
    {
        var first = InviteTo(Old);
        var second = InviteTo("OLD@example.com");
        Holding(first, second);

        var changed = await _changer.ChangeAddressAsync(Old, New, CancellationToken.None);

        Assert.Equal(2, changed);
        Assert.Equal([New, New], new[] { first.Email, second.Email });
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvitesInEveryStatus_Move_SoAStudentsInvitationsStayTogether()
    {
        var pending = InviteTo(Old);

        var accepted = InviteTo(Old);
        var code = accepted.GenerateCode(72, Now).Code;
        accepted.Accept(code, Guid.NewGuid(), Old, Now);

        var declined = InviteTo(Old);
        declined.Decline(Old, Now);

        var revoked = InviteTo(Old);
        revoked.Revoke(Now);
        Holding(pending, accepted, declined, revoked);

        var changed = await _changer.ChangeAddressAsync(Old, New, CancellationToken.None);

        Assert.Equal(4, changed);
        Assert.All(new[] { pending, accepted, declined, revoked }, invite => Assert.Equal(New, invite.Email));
        // Only the address moved: nobody is enrolled or un-enrolled, and no invite changes status.
        Assert.Equal(
            [InviteStatus.Pending, InviteStatus.Accepted, InviteStatus.Declined, InviteStatus.Revoked],
            new[] { pending.Status, accepted.Status, declined.Status, revoked.Status });
    }

    [Fact]
    public async Task EachChangedInvite_RaisesItsOwnEvent_SoEachIsAudited()
    {
        var first = InviteTo(Old);
        var second = InviteTo(Old);
        Holding(first, second);

        await _changer.ChangeAddressAsync(Old, New, CancellationToken.None);

        Assert.All(new[] { first, second }, invite => Assert.Single(invite.DomainEvents.OfType<InviteEmailChangedEvent>()));
    }

    [Fact]
    public async Task TheAddressesAreTrimmed_AndTheOldOneIsLookedUpAsWritten()
    {
        var invite = InviteTo(Old);
        Holding(invite);

        await _changer.ChangeAddressAsync($"  {Old} ", $" {New}  ", CancellationToken.None);

        Assert.Equal(New, invite.Email);
        await _repository.Received(1).ListByEmailAsync(Old, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("OLD@EXAMPLE.COM")]
    [InlineData(" old@example.com ")]
    public async Task TheSameAddress_HoweverItIsWritten_ChangesNothing(string same)
    {
        var changed = await _changer.ChangeAddressAsync(Old, same, CancellationToken.None);

        Assert.Equal(0, changed);
        await _repository.DidNotReceiveWithAnyArgs().ListByEmailAsync(default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenNoInviteWasSentToTheAddress_ChangesNothing_SoARepeatedRequestIsHarmless()
    {
        Holding();

        var changed = await _changer.ChangeAddressAsync(Old, New, CancellationToken.None);

        Assert.Equal(0, changed);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(Old, "not-an-email")]
    [InlineData(Old, "spaces in@example.com")]
    [InlineData(Old, "")]
    public async Task ANewAddressThatIsNotValid_IsRefused_AndNothingIsChanged(string current, string replacement)
    {
        await Assert.ThrowsAsync<InvalidInviteEmailError>(() => _changer.ChangeAddressAsync(current, replacement, CancellationToken.None));

        await _repository.DidNotReceiveWithAnyArgs().ListByEmailAsync(default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public async Task ACurrentAddressThatCannotHaveBeenInvited_MatchesNothing_InsteadOfFailing(string current)
    {
        // An account's stored address is not checked for its shape, and moving the account's invitations must not fail on that.
        _repository.ListByEmailAsync(current.Trim(), Arg.Any<CancellationToken>()).Returns(Array.Empty<InviteAggregate>());

        var changed = await _changer.ChangeAddressAsync(current, New, CancellationToken.None);

        Assert.Equal(0, changed);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
