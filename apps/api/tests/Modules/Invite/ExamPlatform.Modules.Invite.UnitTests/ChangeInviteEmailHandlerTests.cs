using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>Staff changing the address on a student's invitations: what they are told when it does something, and when it does not.</summary>
public class ChangeInviteEmailHandlerTests
{
    private readonly IInviteAddressChanger _changer = Substitute.For<IInviteAddressChanger>();
    private readonly ChangeInviteEmailHandler _handler;

    public ChangeInviteEmailHandlerTests() => _handler = new ChangeInviteEmailHandler(_changer);

    private Task<ExamPlatform.Modules.Invite.Application.Dtos.ChangeInviteEmailResultDto> Handle(string? current, string? replacement) =>
        _handler.HandleAsync(new ChangeInviteEmailCommand(current, replacement), CancellationToken.None);

    [Fact]
    public async Task ReportsHowManyInvitationsWereChanged()
    {
        _changer.ChangeAddressAsync("old@example.com", "new@example.com", Arg.Any<CancellationToken>()).Returns(3);

        var result = await Handle("  old@example.com", "new@example.com ");

        Assert.Equal(3, result.Changed);
    }

    [Fact]
    public async Task NoInvitationsToThatAddress_Is404_NotASilentZero()
    {
        _changer.ChangeAddressAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(0);

        var error = await Assert.ThrowsAsync<NoInvitesForEmailError>(() => Handle("old@example.com", "new@example.com"));

        Assert.Equal(404, error.HttpStatusCode);
        Assert.DoesNotContain("old@example.com", error.Message);
    }

    [Theory]
    [InlineData("old@example.com", "OLD@example.com")]
    [InlineData("old@example.com", " old@example.com ")]
    public async Task TheSameAddress_Is400_AndIsNotLookedFor(string current, string replacement)
    {
        var error = await Assert.ThrowsAsync<InviteEmailUnchangedError>(() => Handle(current, replacement));

        Assert.Equal(400, error.HttpStatusCode);
        await _changer.DidNotReceiveWithAnyArgs().ChangeAddressAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(null, "new@example.com")]
    [InlineData("old@example.com", null)]
    [InlineData("old@example.com", "not-an-email")]
    public async Task AMissingOrInvalidAddress_Is400(string? current, string? replacement)
    {
        var error = await Assert.ThrowsAsync<InvalidInviteEmailError>(() => Handle(current, replacement));

        Assert.Equal(400, error.HttpStatusCode);
        await _changer.DidNotReceiveWithAnyArgs().ChangeAddressAsync(default!, default!, default);
    }
}
