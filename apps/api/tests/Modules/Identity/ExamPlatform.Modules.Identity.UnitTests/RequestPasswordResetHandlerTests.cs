using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class RequestPasswordResetHandlerTests
{
    private const string Email = "staff@example.com";

    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly User _user = User.Register(Email, null, new DateOnly(1990, 1, 1), "Staff", Now.AddDays(-30));
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordResetTokenRepository _tokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IOtpSender _sender = Substitute.For<IOtpSender>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly RequestPasswordResetHandler _handler;

    public RequestPasswordResetHandlerTests()
    {
        _userRepository.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(_user);
        _handler = new RequestPasswordResetHandler(_userRepository, _tokenRepository, _sender, _unitOfWork, new FakeClock(Now));
    }

    [Fact]
    public async Task HandleAsync_UserWithoutPassword_IssuesAndSendsNothing()
    {
        await _handler.HandleAsync(new RequestPasswordResetCommand(Email), CancellationToken.None);

        AssertNothingIssued();
    }

    [Fact]
    public async Task HandleAsync_UnknownEmail_IssuesAndSendsNothing()
    {
        await _handler.HandleAsync(new RequestPasswordResetCommand("nobody@example.com"), CancellationToken.None);

        AssertNothingIssued();
    }

    [Fact]
    public async Task HandleAsync_RevokesEarlierOutstandingTokens()
    {
        _user.SetPasswordHash("hashed-password");

        await _handler.HandleAsync(new RequestPasswordResetCommand(Email), CancellationToken.None);

        // The earlier links are revoked before the new one is added, so the new one survives.
        Received.InOrder(() =>
        {
            _tokenRepository.RevokeOutstandingForUserAsync(_user.Id, Now, Arg.Any<CancellationToken>());
            _tokenRepository.AddAsync(
                Arg.Is<PasswordResetToken>(t => t.UserId == _user.Id && t.IsUsable(Now)), Arg.Any<CancellationToken>());
        });
        await _tokenRepository.DidNotReceiveWithAnyArgs().GetOutstandingForUserAsync(default, default, default);
        await _sender.Received(1).SendAsync(OtpChannel.Email, Email, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private void AssertNothingIssued()
    {
        Assert.Empty(_tokenRepository.ReceivedCalls());
        Assert.Empty(_sender.ReceivedCalls());
        Assert.Empty(_unitOfWork.ReceivedCalls());
    }
}
