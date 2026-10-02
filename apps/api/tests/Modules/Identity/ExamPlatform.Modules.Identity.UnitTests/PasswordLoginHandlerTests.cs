using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class PasswordLoginHandlerTests
{
    private const string Email = "staff@example.com";
    private const string Password = "correct-password";

    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly User _user = User.Register(Email, null, new DateOnly(1990, 1, 1), "Staff", Now);
    private readonly IOtpChallengeRepository _challengeRepository = Substitute.For<IOtpChallengeRepository>();
    private readonly IOtpSender _sender = Substitute.For<IOtpSender>();
    private readonly ISignInDiagnostics _diagnostics = Substitute.For<ISignInDiagnostics>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ITokenGenerator _tokenGenerator = Substitute.For<ITokenGenerator>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly PasswordLoginHandler _handler;

    public PasswordLoginHandlerTests()
    {
        _user.SetPasswordHash("hashed-" + Password);
        _user.Activate();

        _userRepository.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(_user);
        var passwordHasher = Substitute.For<IPasswordHasher>();
        passwordHasher.Verify(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => "hashed-" + call.ArgAt<string>(0) == call.ArgAt<string>(1));
        _challengeRepository
            .GetOutstandingAsync(Arg.Any<string>(), Arg.Any<OtpPurpose>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OtpChallenge>());
        var codeGenerator = Substitute.For<IOtpCodeGenerator>();
        codeGenerator.GenerateCode().Returns("123456");
        codeGenerator.Hash(Arg.Any<string>()).Returns(call => "hashed-" + call.Arg<string>());
        _tokenGenerator.GenerateAccessToken(Arg.Any<User>(), Arg.Any<UserSession>()).Returns("access-token");

        var clock = new FakeClock(Now);
        _handler = new PasswordLoginHandler(
            _userRepository,
            passwordHasher,
            new LoginEligibilityPolicy(),
            new OtpChallengeIssuer(_challengeRepository, codeGenerator, _sender, clock),
            new LoginSessionIssuer(_tokenGenerator, clock),
            _diagnostics,
            _unitOfWork);
    }

    private static PasswordLoginCommand Command() => new(Email, Password, null, null);

    [Fact]
    public async Task HandleAsync_UnknownEmail_IsRefusedLikeAnyOtherBadLogin_AndTheDeveloperIsToldWhy()
    {
        _userRepository.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAsync<InvalidCredentialsError>(() => _handler.HandleAsync(Command(), CancellationToken.None));

        _diagnostics.Received(1).Explain(SignInHint.NoAccountForAddress, OtpChannel.Email, Email);
    }

    [Fact]
    public async Task HandleAsync_AccountWithNoPassword_IsRefused_AndTheDeveloperIsToldCandidatesUseACode()
    {
        // Every candidate registers without a password, so a candidate typing one gets nothing, silently, unless told.
        var candidate = User.Register(Email, null, new DateOnly(2000, 1, 1), "Candidate", Now);
        candidate.AssignRole(Role.Create("Candidate", requiresTwoFactor: false));
        candidate.Activate();
        _userRepository.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(candidate);

        await Assert.ThrowsAsync<InvalidCredentialsError>(() => _handler.HandleAsync(Command(), CancellationToken.None));

        _diagnostics.Received(1).Explain(SignInHint.CandidateHasNoPassword, OtpChannel.Email, Email);
    }

    [Fact]
    public async Task HandleAsync_WrongPassword_IsRefused_AndTheDeveloperIsToldWhy()
    {
        await Assert.ThrowsAsync<InvalidCredentialsError>(
            () => _handler.HandleAsync(new PasswordLoginCommand(Email, "not-the-password", null, null), CancellationToken.None));

        _diagnostics.Received(1).Explain(SignInHint.WrongPassword, OtpChannel.Email, Email);
    }

    [Fact]
    public async Task HandleAsync_CorrectPassword_HasNothingToExplain()
    {
        await _handler.HandleAsync(Command(), CancellationToken.None);

        _diagnostics.DidNotReceiveWithAnyArgs().Explain(default, default, default!);
    }

    [Fact]
    public async Task HandleAsync_SuspendedAccountWithCorrectPassword_ThrowsAccountLockedError()
    {
        _user.AssignRole(Role.Create("SuperAdmin", requiresTwoFactor: true));
        _user.Suspend(Now);

        await Assert.ThrowsAsync<AccountLockedError>(() => _handler.HandleAsync(Command(), CancellationToken.None));

        // Refused before any second-factor code is issued or session started.
        await _challengeRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default!, default);
        Assert.Empty(_user.Sessions);
    }

    [Fact]
    public async Task HandleAsync_TwoFactorUser_IssuesTwoFactorStepChallengeInsteadOfSession()
    {
        _user.AssignRole(Role.Create("SuperAdmin", requiresTwoFactor: true));

        var result = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.RequiresTwoFactor);
        Assert.Null(result.AccessToken);
        await _challengeRepository.Received(1).AddAsync(
            Arg.Is<OtpChallenge>(c =>
                c.Id == result.OtpChallengeId && c.UserId == _user.Id && c.Purpose == OtpPurpose.TwoFactorStep),
            Arg.Any<CancellationToken>());
        Assert.Empty(_user.Sessions);
    }
}
