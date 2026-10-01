using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class RequestOtpHandlerTests
{
    [Fact]
    public async Task HandleAsync_UnknownDestination_ThrowsUserNotFoundError()
    {
        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetByEmailAsync("nobody@example.com", Arg.Any<CancellationToken>()).Returns((User?)null);

        var issuer = new OtpChallengeIssuer(
            Substitute.For<IOtpChallengeRepository>(),
            Substitute.For<IOtpCodeGenerator>(),
            Substitute.For<IOtpSender>(),
            new FakeClock(DateTime.UtcNow));

        var handler = new RequestOtpHandler(userRepository, issuer, Substitute.For<IIdentityUnitOfWork>());

        var command = new RequestOtpCommand(OtpChannel.Email, "nobody@example.com");

        await Assert.ThrowsAsync<UserNotFoundError>(() => handler.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_KnownDestination_IssuesChallengeAndSendsCode()
    {
        var user = User.Register("candidate@example.com", null, new DateOnly(2000, 1, 1), "Candidate", DateTime.UtcNow);
        var userRepository = Substitute.For<IUserRepository>();
        userRepository.GetByEmailAsync("candidate@example.com", Arg.Any<CancellationToken>()).Returns(user);

        var challengeRepository = Substitute.For<IOtpChallengeRepository>();
        challengeRepository
            .GetOutstandingAsync("candidate@example.com", OtpPurpose.Login, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OtpChallenge>());
        var codeGenerator = Substitute.For<IOtpCodeGenerator>();
        codeGenerator.GenerateCode().Returns("123456");
        codeGenerator.Hash("123456").Returns("hashed-123456");
        var sender = Substitute.For<IOtpSender>();

        var issuer = new OtpChallengeIssuer(challengeRepository, codeGenerator, sender, new FakeClock(DateTime.UtcNow));
        var unitOfWork = Substitute.For<IIdentityUnitOfWork>();
        var handler = new RequestOtpHandler(userRepository, issuer, unitOfWork);

        var challengeId = await handler.HandleAsync(
            new RequestOtpCommand(OtpChannel.Email, "candidate@example.com"), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, challengeId);
        await challengeRepository.Received(1).AddAsync(
            Arg.Is<OtpChallenge>(c => c.UserId == user.Id), Arg.Any<CancellationToken>());
        await sender.Received(1).SendAsync(OtpChannel.Email, "candidate@example.com", "123456", Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
