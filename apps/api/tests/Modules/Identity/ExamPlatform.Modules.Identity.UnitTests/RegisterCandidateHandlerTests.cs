using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class RegisterCandidateHandlerTests
{
    private const string Email = "candidate@example.com";
    private const string PhoneNumber = "+919800000000";
    private const string Code = "123456";

    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roleRepository = Substitute.For<IRoleRepository>();
    private readonly IOtpChallengeRepository _challengeRepository = Substitute.For<IOtpChallengeRepository>();
    private readonly IOtpSender _sender = Substitute.For<IOtpSender>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly RegisterCandidateHandler _handler;

    public RegisterCandidateHandlerTests()
    {
        _roleRepository.GetByNameAsync(RegisterCandidateHandler.CandidateRoleName, Arg.Any<CancellationToken>())
            .Returns(Role.Create(RegisterCandidateHandler.CandidateRoleName, requiresTwoFactor: false));
        _challengeRepository
            .GetOutstandingAsync(Arg.Any<string>(), Arg.Any<OtpPurpose>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OtpChallenge>());
        var codeGenerator = Substitute.For<IOtpCodeGenerator>();
        codeGenerator.GenerateCode().Returns(Code);
        codeGenerator.Hash(Arg.Any<string>()).Returns(call => "hashed-" + call.Arg<string>());

        var clock = new FakeClock(Now);
        _handler = new RegisterCandidateHandler(
            _userRepository,
            _roleRepository,
            new OtpChallengeIssuer(_challengeRepository, codeGenerator, _sender, clock),
            _unitOfWork,
            clock);
    }

    private static RegisterCandidateCommand Command(string? email, string? phoneNumber, OtpChannel channel) =>
        new(email, phoneNumber, new DateOnly(2000, 1, 1), "Test Candidate", channel);

    // Asserts the request was refused before it looked anything up, added anything or sent anything.
    private async Task AssertNothingTouchedAsync()
    {
        Assert.Empty(_userRepository.ReceivedCalls());
        Assert.Empty(_roleRepository.ReceivedCalls());
        await _challengeRepository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task HandleAsync_MissingDateOfBirth_ThrowsAndAddsNoUser()
    {
        var command = Command(Email, null, OtpChannel.Email) with { DateOfBirth = null };

        var error = await Assert.ThrowsAsync<InvalidDateOfBirthError>(
            () => _handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(InvalidDateOfBirthError.Missing().Message, error.Message);
        await AssertNothingTouchedAsync();
    }

    [Fact]
    public async Task HandleAsync_EmailChannelWithoutEmail_ThrowsContactChannelMismatchError()
    {
        await Assert.ThrowsAsync<ContactChannelMismatchError>(
            () => _handler.HandleAsync(Command(null, PhoneNumber, OtpChannel.Email), CancellationToken.None));

        await AssertNothingTouchedAsync();
    }

    [Fact]
    public async Task HandleAsync_SmsChannelWithBlankPhone_ThrowsContactChannelMismatchError()
    {
        await Assert.ThrowsAsync<ContactChannelMismatchError>(
            () => _handler.HandleAsync(Command(Email, "  ", OtpChannel.Sms), CancellationToken.None));

        await AssertNothingTouchedAsync();
    }

    [Fact]
    public async Task HandleAsync_SmsChannel_AddsCandidateAndSendsTheCodeToThePhone()
    {
        var challengeId = await _handler.HandleAsync(Command(Email, PhoneNumber, OtpChannel.Sms), CancellationToken.None);

        await _userRepository.Received(1).AddAsync(
            Arg.Is<User>(u => u.Email == Email && u.PhoneNumber == PhoneNumber), Arg.Any<CancellationToken>());
        await _challengeRepository.Received(1).AddAsync(
            Arg.Is<OtpChallenge>(c =>
                c.Id == challengeId && c.Destination == PhoneNumber && c.Purpose == OtpPurpose.Registration),
            Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(OtpChannel.Sms, PhoneNumber, Code, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
