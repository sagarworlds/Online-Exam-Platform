using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class VerifyOtpHandlerTests
{
    private const string Destination = "candidate@example.com";
    private const string CorrectCode = "123456";
    private const string WrongCode = "654321";

    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly User _user = User.Register(Destination, null, new DateOnly(2000, 1, 1), "Candidate", Now);
    private readonly IOtpChallengeRepository _challengeRepository = Substitute.For<IOtpChallengeRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IOtpCodeGenerator _codeGenerator = Substitute.For<IOtpCodeGenerator>();
    private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly OtpChallenge _challenge;
    private readonly VerifyOtpHandler _handler;

    public VerifyOtpHandlerTests()
    {
        _challenge = IssueChallenge(OtpPurpose.Login);
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _codeGenerator.Hash(Arg.Any<string>()).Returns(call => "hashed-" + call.Arg<string>());
        _tokenGenerator.GenerateAccessToken(Arg.Any<User>(), Arg.Any<UserSession>()).Returns("access-token");

        var clock = new FakeClock(Now.AddMinutes(1));
        _handler = new VerifyOtpHandler(
            _challengeRepository,
            _userRepository,
            _codeGenerator,
            new LoginEligibilityPolicy(),
            new LoginSessionIssuer(_tokenGenerator, clock),
            _unitOfWork,
            clock);
    }

    private static VerifyOtpCommand Command(Guid challengeId, string code) => new(challengeId, code, null, null);

    // A challenge for _user with the correct code, findable by id through the repository.
    private OtpChallenge IssueChallenge(OtpPurpose purpose)
    {
        var challenge = OtpChallenge.Issue(
            _user.Id, OtpChannel.Email, Destination, "hashed-" + CorrectCode, purpose, Now, TimeSpan.FromMinutes(10));
        _challengeRepository.GetByIdAsync(challenge.Id, Arg.Any<CancellationToken>()).Returns(challenge);
        return challenge;
    }

    [Fact]
    public async Task HandleAsync_WrongCode_SavesAttemptBeforeThrowingOtpMismatchError()
    {
        int? attemptCountWhenSaved = null;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            attemptCountWhenSaved = _challenge.AttemptCount;
            return 1;
        });

        await Assert.ThrowsAsync<OtpMismatchError>(
            () => _handler.HandleAsync(Command(_challenge.Id, WrongCode), CancellationToken.None));

        // Saved exactly once, and only after the failed attempt was counted, so the
        // count survives the error response.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(1, attemptCountWhenSaved);
        _tokenGenerator.DidNotReceiveWithAnyArgs().GenerateAccessToken(default!, default!);
    }

    [Fact]
    public async Task HandleAsync_ConsumedChallenge_ThrowsOtpAlreadyUsedError_AndMintsNoToken()
    {
        Assert.Equal(OtpVerificationOutcome.Verified, _challenge.Verify("hashed-" + CorrectCode, Now));

        await Assert.ThrowsAsync<OtpAlreadyUsedError>(
            () => _handler.HandleAsync(Command(_challenge.Id, CorrectCode), CancellationToken.None));

        _tokenGenerator.DidNotReceiveWithAnyArgs().GenerateAccessToken(default!, default!);
        Assert.Empty(_user.Sessions);
    }

    [Fact]
    public async Task HandleAsync_CorrectCode_ConsumesChallengeAndIssuesSession()
    {
        var result = await _handler.HandleAsync(Command(_challenge.Id, CorrectCode), CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.True(_challenge.IsConsumed);
        var session = Assert.Single(_user.Sessions);
        Assert.Equal(session.Id, result.SessionId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_LoginPurposeForTwoFactorUser_ThrowsTwoFactorLoginRequired_AndPersistsConsumption()
    {
        _user.AssignRole(Role.Create("SuperAdmin", requiresTwoFactor: true));
        bool? consumedWhenSaved = null;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            consumedWhenSaved = _challenge.IsConsumed;
            return 1;
        });

        await Assert.ThrowsAsync<TwoFactorLoginRequiredError>(
            () => _handler.HandleAsync(Command(_challenge.Id, CorrectCode), CancellationToken.None));

        // The consumed challenge was saved before the refusal, so the same code cannot be
        // replayed later, and no session was started for the staff account (FR-3).
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.True(consumedWhenSaved);
        Assert.Empty(_user.Sessions);
        _tokenGenerator.DidNotReceiveWithAnyArgs().GenerateAccessToken(default!, default!);
    }

    [Fact]
    public async Task HandleAsync_TwoFactorStepForTwoFactorUser_IssuesSession()
    {
        _user.AssignRole(Role.Create("SuperAdmin", requiresTwoFactor: true));
        var twoFactorChallenge = IssueChallenge(OtpPurpose.TwoFactorStep);

        var result = await _handler.HandleAsync(Command(twoFactorChallenge.Id, CorrectCode), CancellationToken.None);

        Assert.Equal("access-token", result.AccessToken);
        Assert.True(twoFactorChallenge.IsConsumed);
        Assert.Single(_user.Sessions);
    }

    [Fact]
    public async Task HandleAsync_SuspendedUser_ThrowsAccountLockedError()
    {
        _user.Suspend(Now);

        await Assert.ThrowsAsync<AccountLockedError>(
            () => _handler.HandleAsync(Command(_challenge.Id, CorrectCode), CancellationToken.None));

        Assert.True(_challenge.IsConsumed);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Empty(_user.Sessions);
    }

    [Fact]
    public async Task HandleAsync_PasswordResetPurpose_ThrowsOtpPurposeNotAllowedError()
    {
        var resetChallenge = IssueChallenge(OtpPurpose.PasswordReset);

        await Assert.ThrowsAsync<OtpPurposeNotAllowedError>(
            () => _handler.HandleAsync(Command(resetChallenge.Id, CorrectCode), CancellationToken.None));

        Assert.Empty(_user.Sessions);
    }

    [Fact]
    public async Task HandleAsync_RegistrationPurpose_ActivatesUser()
    {
        var registrationChallenge = IssueChallenge(OtpPurpose.Registration);
        Assert.Equal(UserStatus.PendingVerification, _user.Status);

        var result = await _handler.HandleAsync(Command(registrationChallenge.Id, CorrectCode), CancellationToken.None);

        Assert.Equal(UserStatus.Active, _user.Status);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Single(_user.Sessions);
    }
}
