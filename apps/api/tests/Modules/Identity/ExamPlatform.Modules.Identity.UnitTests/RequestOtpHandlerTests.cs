using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class RequestOtpHandlerTests
{
    private const string Destination = "candidate@example.com";
    private const string Code = "123456";

    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IOtpChallengeRepository _challengeRepository = Substitute.For<IOtpChallengeRepository>();
    private readonly IOtpCodeGenerator _codeGenerator = Substitute.For<IOtpCodeGenerator>();
    private readonly IOtpSender _sender = Substitute.For<IOtpSender>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly RequestOtpHandler _handler;

    public RequestOtpHandlerTests()
    {
        _challengeRepository
            .GetOutstandingAsync(Arg.Any<string>(), Arg.Any<OtpPurpose>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OtpChallenge>());
        _codeGenerator.GenerateCode().Returns(Code);
        _codeGenerator.Hash(Arg.Any<string>()).Returns(call => "hashed-" + call.Arg<string>());

        var issuer = new OtpChallengeIssuer(_challengeRepository, _codeGenerator, _sender, new FakeClock(Now));
        _handler = new RequestOtpHandler(_userRepository, new LoginEligibilityPolicy(), issuer, _unitOfWork);
    }

    private static RequestOtpCommand Command() => new(OtpChannel.Email, Destination);

    // A registered user for Destination that the repository finds by email.
    private User ArrangeUser(Action<User>? configure = null)
    {
        var user = User.Register(Destination, null, new DateOnly(2000, 1, 1), "Candidate", Now);
        user.AssignRole(Role.Create("Candidate", requiresTwoFactor: false));
        configure?.Invoke(user);
        _userRepository.GetByEmailAsync(Destination, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    // Asserts the request was answered with a persisted decoy: a challenge with no user,
    // a code hash no 6-digit code produces, nothing sent, and the challenge's own id returned.
    private async Task AssertDecoyIssuedAsync(Guid challengeId)
    {
        await _challengeRepository.Received(1).AddAsync(
            Arg.Is<OtpChallenge>(c => c.Id == challengeId && c.UserId == null && c.CodeHash != "hashed-" + Code),
            Arg.Any<CancellationToken>());
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default!, default);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnknownDestination_ReturnsChallengeIdWithoutSending()
    {
        _userRepository.GetByEmailAsync(Destination, Arg.Any<CancellationToken>()).Returns((User?)null);

        var challengeId = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, challengeId);
        await AssertDecoyIssuedAsync(challengeId);
    }

    [Fact]
    public async Task HandleAsync_SuspendedUser_IssuesDecoyWithoutSending()
    {
        ArrangeUser(user => user.Suspend(Now));

        var challengeId = await _handler.HandleAsync(Command(), CancellationToken.None);

        await AssertDecoyIssuedAsync(challengeId);
    }

    [Fact]
    public async Task HandleAsync_TwoFactorUser_IssuesDecoyWithoutSending()
    {
        ArrangeUser(user =>
        {
            user.AssignRole(Role.Create("SuperAdmin", requiresTwoFactor: true));
            user.Activate();
        });

        var challengeId = await _handler.HandleAsync(Command(), CancellationToken.None);

        // Staff must sign in with password + 2FA, so this path never sends them a code.
        await AssertDecoyIssuedAsync(challengeId);
    }

    [Fact]
    public async Task HandleAsync_PendingUser_IssuesRegistrationPurposeChallenge()
    {
        var user = ArrangeUser();
        Assert.Equal(UserStatus.PendingVerification, user.Status);

        var challengeId = await _handler.HandleAsync(Command(), CancellationToken.None);

        await _challengeRepository.Received(1).AddAsync(
            Arg.Is<OtpChallenge>(c => c.Id == challengeId && c.UserId == user.Id && c.Purpose == OtpPurpose.Registration),
            Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(OtpChannel.Email, Destination, Code, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ActiveUser_IssuesLoginChallengeAndSendsCode()
    {
        var user = ArrangeUser(u => u.Activate());

        var challengeId = await _handler.HandleAsync(Command(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, challengeId);
        await _challengeRepository.Received(1).AddAsync(
            Arg.Is<OtpChallenge>(c =>
                c.Id == challengeId && c.UserId == user.Id && c.Purpose == OtpPurpose.Login && c.CodeHash == "hashed-" + Code),
            Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(OtpChannel.Email, Destination, Code, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
