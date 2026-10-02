using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class LogoutHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly User _user = User.Register("candidate@example.com", null, new DateOnly(2000, 1, 1), "Candidate", Now);
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly LogoutHandler _handler;

    public LogoutHandlerTests()
    {
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _handler = new LogoutHandler(_userRepository, _unitOfWork, new FakeClock(Now.AddMinutes(10)));
    }

    [Fact]
    public async Task HandleAsync_RevokesAndSaves()
    {
        var session = _user.StartNewSession("hash-1", Now, Now.AddHours(1), null, null);

        await _handler.HandleAsync(new LogoutCommand(_user.Id, session.Id), CancellationToken.None);

        Assert.Equal(SessionRevocationReason.LoggedOut, session.RevokedReason);
        Assert.Equal(Now.AddMinutes(10), session.RevokedAtUtc);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UnknownUser_ThrowsUserNotFoundError()
    {
        await Assert.ThrowsAsync<UserNotFoundError>(
            () => _handler.HandleAsync(new LogoutCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task HandleAsync_SessionTheUserDoesNotHave_ThrowsSessionNotFoundError()
    {
        var session = _user.StartNewSession("hash-1", Now, Now.AddHours(1), null, null);

        await Assert.ThrowsAsync<SessionNotFoundError>(
            () => _handler.HandleAsync(new LogoutCommand(_user.Id, Guid.NewGuid()), CancellationToken.None));

        Assert.Null(session.RevokedAtUtc);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
