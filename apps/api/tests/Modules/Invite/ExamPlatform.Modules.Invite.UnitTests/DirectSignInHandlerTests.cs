using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Events;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>
/// Opening a direct sign-in link. Nobody is signed in, so the link is the only proof: everything that can refuse runs before the
/// single-use code is spent, and the code is spent before the session starts.
/// </summary>
public class DirectSignInHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();
    private readonly IInviteUnitOfWork _unitOfWork = Substitute.For<IInviteUnitOfWork>();
    private readonly IDirectSignIn _directSignIn = Substitute.For<IDirectSignIn>();
    private readonly DirectSignInOptions _options = new();
    private readonly InviteAggregate _invite = new(Guid.NewGuid(), null, "student@example.com", Guid.NewGuid(), Now);
    private readonly InviteCode _direct;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DirectSignInHandler _handler;

    public DirectSignInHandlerTests()
    {
        _direct = _invite.GenerateCode(72, Now, allowsDirectSignIn: true);
        _repository.GetByCodeAsync(_direct.Code, Arg.Any<CancellationToken>()).Returns(_invite);
        _directSignIn.FindOrCreateStudentAsync(_invite.Email, Arg.Any<CancellationToken>())
            .Returns(new DirectSignInAccount(_userId, Created: false));
        _directSignIn.StartSessionAsync(_userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DirectSignInSession("access-token", Guid.Parse("11111111-1111-1111-1111-111111111111")));

        _handler = new DirectSignInHandler(_repository, _unitOfWork, _directSignIn, Options.Create(_options), new FakeClock(Now));
    }

    private Task<Application.Dtos.DirectSignInResultDto> OpenAsync(string? code = null, string? fingerprint = "device-1", string? ip = "203.0.113.5") =>
        _handler.HandleAsync(new DirectSignInCommand(code ?? _direct.Code, fingerprint, ip), CancellationToken.None);

    [Fact]
    public async Task OpeningTheLink_SignsTheStudentIn_AcceptsTheInvitation_AndNamesTheExamToOpen()
    {
        var result = await OpenAsync();

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.SessionId);
        Assert.Equal(_invite.ExamId, result.ExamId);
        Assert.Equal(InviteStatus.Accepted, _invite.Status);
        Assert.Equal(_userId, _invite.AcceptedByUserId);
        Assert.Equal(Now, _direct.UsedAt);
    }

    [Fact]
    public async Task TheCodeIsSpentAndSaved_BeforeTheSessionStarts()
    {
        await OpenAsync();

        Received.InOrder(() =>
        {
            _directSignIn.FindOrCreateStudentAsync(_invite.Email, Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _directSignIn.StartSessionAsync(_userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task TheSessionRecordsWhereTheLinkWasOpenedFrom()
    {
        await OpenAsync(fingerprint: "device-9", ip: "198.51.100.4");

        await _directSignIn.Received(1).StartSessionAsync(_userId, "device-9", "198.51.100.4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAccountTheLinkMade_IsReportedToTheAuditTrail_AsCreated()
    {
        _directSignIn.FindOrCreateStudentAsync(_invite.Email, Arg.Any<CancellationToken>())
            .Returns(new DirectSignInAccount(_userId, Created: true));

        await OpenAsync();

        var used = Assert.Single(_invite.DomainEvents.OfType<InviteDirectLinkUsedEvent>());
        Assert.True(used.AccountCreated);
        Assert.Equal(_userId, used.UserId);
    }

    [Fact]
    public async Task TheCode_IsReadWithoutRegardToCaseOrSpaces()
    {
        await OpenAsync($"  {_direct.Code.ToLowerInvariant()}  ");

        await _repository.Received(1).GetByCodeAsync(_direct.Code, Arg.Any<CancellationToken>());
        Assert.Equal(InviteStatus.Accepted, _invite.Status);
    }

    // ---- refusals that leave the code unspent -----------------------------------------------------

    [Fact]
    public async Task ARefusedAccount_LeavesTheCodeUnspent_SavesNothing_AndStartsNoSession()
    {
        _directSignIn.FindOrCreateStudentAsync(_invite.Email, Arg.Any<CancellationToken>()).ThrowsAsync(new AccountLocked());

        await Assert.ThrowsAsync<AccountLocked>(() => OpenAsync());

        Assert.Null(_direct.UsedAt);
        Assert.Equal(InviteStatus.Pending, _invite.Status);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _directSignIn.DidNotReceiveWithAnyArgs().StartSessionAsync(default, default, default, default);
    }

    [Fact]
    public async Task AnOrdinaryCode_CannotSignAnyoneIn_AndNoAccountIsLookedUp()
    {
        var ordinary = _invite.GenerateCode(24, Now);
        _repository.GetByCodeAsync(ordinary.Code, Arg.Any<CancellationToken>()).Returns(_invite);

        var error = await Assert.ThrowsAsync<InvalidInviteCodeError>(() => OpenAsync(ordinary.Code));

        Assert.Equal(400, error.HttpStatusCode);
        await _directSignIn.DidNotReceiveWithAnyArgs().FindOrCreateStudentAsync(default!, default);
        Assert.Null(ordinary.UsedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOSUCHCODE000000")]
    public async Task AMissingOrUnknownCode_IsInvalid_AndNothingIsLookedUp(string? code)
    {
        _repository.GetByCodeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((InviteAggregate?)null);

        await Assert.ThrowsAsync<InvalidInviteCodeError>(() => _handler.HandleAsync(new DirectSignInCommand(code, null, null), CancellationToken.None));

        await _directSignIn.DidNotReceiveWithAnyArgs().FindOrCreateStudentAsync(default!, default);
    }

    [Fact]
    public async Task ASecondClick_IsInvalid_SoTheLinkSignsInOnlyOnce()
    {
        await OpenAsync();

        await Assert.ThrowsAsync<InvalidInviteCodeError>(() => OpenAsync());

        await _directSignIn.Received(1).StartSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInviteAlreadyAcceptedAnotherWay_IsAStateError_AndNoAccountIsMade()
    {
        var ordinary = _invite.GenerateCode(24, Now);
        _invite.Accept(ordinary.Code, Guid.NewGuid(), _invite.Email, Now);

        var error = await Assert.ThrowsAsync<InviteStateError>(() => OpenAsync());

        Assert.Equal(409, error.HttpStatusCode);
        await _directSignIn.DidNotReceiveWithAnyArgs().FindOrCreateStudentAsync(default!, default);
    }

    [Fact]
    public async Task WhenSwitchedOff_EveryLinkIsRefused_BeforeTheCodeIsEvenLookedUp()
    {
        _options.Enabled = false;

        var error = await Assert.ThrowsAsync<DirectSignInDisabledError>(() => OpenAsync());

        Assert.Equal(403, error.HttpStatusCode);
        await _repository.DidNotReceiveWithAnyArgs().GetByCodeAsync(default!, default);
    }

    // ---- a failure after the code is spent --------------------------------------------------------

    [Fact]
    public async Task IfTheSessionCannotStart_TheInvitationStaysAccepted_SoTheStudentCanSignInTheOrdinaryWay()
    {
        _directSignIn.StartSessionAsync(_userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("session store unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => OpenAsync());

        Assert.Equal(InviteStatus.Accepted, _invite.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class AccountLocked() : DomainException("This account cannot currently log in.")
    {
        public override string ErrorCode => "account_locked";

        public override int HttpStatusCode => 403;
    }
}
