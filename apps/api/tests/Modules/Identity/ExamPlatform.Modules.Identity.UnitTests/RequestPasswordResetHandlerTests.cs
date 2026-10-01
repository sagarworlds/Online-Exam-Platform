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
        _tokenRepository.GetOutstandingForUserAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PasswordResetToken>());
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
        var earlier = PasswordResetToken.Issue(_user.Id, "earlier-hash", Now.AddMinutes(-10), TimeSpan.FromMinutes(30));
        _tokenRepository.GetOutstandingForUserAsync(_user.Id, Now, Arg.Any<CancellationToken>()).Returns([earlier]);

        await _handler.HandleAsync(new RequestPasswordResetCommand(Email), CancellationToken.None);

        Assert.Equal(Now, earlier.RevokedAtUtc);
        await _tokenRepository.Received(1).AddAsync(
            Arg.Is<PasswordResetToken>(t => t.UserId == _user.Id && t.IsUsable(Now)), Arg.Any<CancellationToken>());
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
