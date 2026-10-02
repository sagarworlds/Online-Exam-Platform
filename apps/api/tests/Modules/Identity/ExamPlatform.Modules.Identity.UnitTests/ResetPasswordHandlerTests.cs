using System.Security.Cryptography;
using System.Text;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class ResetPasswordHandlerTests
{
    private const string RawToken = "raw-reset-token";
    private const string StrongPassword = "correct horse battery staple";

    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly User _user = User.Register("staff@example.com", null, new DateOnly(1990, 1, 1), "Staff", Now.AddDays(-30));
    private readonly PasswordResetToken _token;
    private readonly IPasswordResetTokenRepository _tokenRepository = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly ResetPasswordHandler _handler;

    public ResetPasswordHandlerTests()
    {
        _user.SetPasswordHash("hashed-old-password");
        _user.Activate();
        _token = PasswordResetToken.Issue(_user.Id, Sha256Hex(RawToken), Now.AddMinutes(-5), TimeSpan.FromMinutes(30));

        _tokenRepository.GetByIdAsync(_token.Id, Arg.Any<CancellationToken>()).Returns(_token);
        _tokenRepository.GetOutstandingForUserAsync(_user.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([_token]);
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        var passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.Hash(Arg.Any<string>()).Returns(call => "hashed-" + call.Arg<string>());

        _handler = new ResetPasswordHandler(
            _tokenRepository, _userRepository, passwordHasher, new PasswordPolicy(), _unitOfWork, new FakeClock(Now));
    }

    // Mirrors how RequestPasswordResetHandler stores the token secret.
    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private ResetPasswordCommand Command(string newPassword, string token = RawToken) => new(_token.Id, token, newPassword);

    [Fact]
    public async Task HandleAsync_WeakPassword_ThrowsAndLeavesTokenUnconsumed()
    {
        var session = _user.StartNewSession("session-hash", Now.AddMinutes(-1), Now.AddHours(1), null, null);

        await Assert.ThrowsAsync<WeakPasswordError>(() => _handler.HandleAsync(Command("short"), CancellationToken.None));

        Assert.True(_token.IsUsable(Now));
        Assert.Equal("hashed-old-password", _user.PasswordHash);
        Assert.True(session.IsActive(Now));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task HandleAsync_PasswordContainingTheEmail_ThrowsWeakPasswordError() =>
        await Assert.ThrowsAsync<WeakPasswordError>(
            () => _handler.HandleAsync(Command("staff-password-2026"), CancellationToken.None));

    [Fact]
    public async Task HandleAsync_Success_RevokesAllSessionsAndOtherOutstandingTokens()
    {
        var session = _user.StartNewSession("session-hash", Now.AddMinutes(-1), Now.AddHours(1), null, null);
        var otherToken = PasswordResetToken.Issue(_user.Id, "other-hash", Now.AddMinutes(-10), TimeSpan.FromMinutes(30));
        _tokenRepository.GetOutstandingForUserAsync(_user.Id, Now, Arg.Any<CancellationToken>())
            .Returns([otherToken, _token]);

        await _handler.HandleAsync(Command(StrongPassword), CancellationToken.None);

        Assert.Equal("hashed-" + StrongPassword, _user.PasswordHash);
        Assert.Equal(Now, _token.ConsumedAtUtc);
        Assert.Null(_token.RevokedAtUtc);
        Assert.Equal(Now, otherToken.RevokedAtUtc);
        Assert.Equal(SessionRevocationReason.PasswordReset, session.RevokedReason);
        Assert.Equal(Now, session.RevokedAtUtc);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RevokedToken_ThrowsPasswordResetTokenInvalidError()
    {
        _token.Revoke(Now.AddMinutes(-1));

        await Assert.ThrowsAsync<PasswordResetTokenInvalidError>(
            () => _handler.HandleAsync(Command(StrongPassword), CancellationToken.None));

        Assert.Equal("hashed-old-password", _user.PasswordHash);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task HandleAsync_WrongSecret_ThrowsPasswordResetTokenInvalidError() =>
        await Assert.ThrowsAsync<PasswordResetTokenInvalidError>(
            () => _handler.HandleAsync(Command(StrongPassword, token: "not-the-secret"), CancellationToken.None));

    // JSON binding leaves a missing token null despite the non-nullable annotation.
    [Fact]
    public async Task HandleAsync_MissingSecret_ThrowsPasswordResetTokenInvalidError() =>
        await Assert.ThrowsAsync<PasswordResetTokenInvalidError>(
            () => _handler.HandleAsync(Command(StrongPassword, token: null!), CancellationToken.None));

    [Fact]
    public async Task HandleAsync_AccountWithoutPassword_ThrowsPasswordResetTokenInvalidError()
    {
        var candidate = User.Register("candidate@example.com", null, new DateOnly(2000, 1, 1), "Candidate", Now.AddDays(-1));
        var token = PasswordResetToken.Issue(candidate.Id, Sha256Hex(RawToken), Now.AddMinutes(-5), TimeSpan.FromMinutes(30));
        _tokenRepository.GetByIdAsync(token.Id, Arg.Any<CancellationToken>()).Returns(token);
        _userRepository.GetByIdAsync(candidate.Id, Arg.Any<CancellationToken>()).Returns(candidate);

        await Assert.ThrowsAsync<PasswordResetTokenInvalidError>(
            () => _handler.HandleAsync(new ResetPasswordCommand(token.Id, RawToken, StrongPassword), CancellationToken.None));

        Assert.Null(candidate.PasswordHash);
        Assert.True(token.IsUsable(Now));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
