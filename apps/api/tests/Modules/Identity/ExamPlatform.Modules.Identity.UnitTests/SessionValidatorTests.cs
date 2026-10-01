using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Sessions;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class SessionValidatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly ISessionLookup _sessionLookup = Substitute.For<ISessionLookup>();
    private readonly FakeClock _clock = new(Now);
    private readonly SessionValidator _validator;

    public SessionValidatorTests()
    {
        _validator = new SessionValidator(_sessionLookup, new LoginEligibilityPolicy(), _clock);
    }

    private void GivenSession(
        Guid? userId = null,
        DateTime? expiresAtUtc = null,
        SessionRevocationReason? revokedReason = null,
        UserStatus userStatus = UserStatus.Active) =>
        _sessionLookup.FindAsync(_sessionId, Arg.Any<CancellationToken>()).Returns(new SessionSnapshot(
            userId ?? _userId,
            expiresAtUtc ?? Now.AddHours(1),
            revokedReason is null ? null : Now.AddMinutes(-1),
            revokedReason,
            userStatus));

    private Task<SessionValidationResult> ValidateAsync() =>
        _validator.ValidateAsync(_userId, _sessionId, CancellationToken.None);

    [Fact]
    public async Task ValidateAsync_ActiveSession_ReturnsValid()
    {
        GivenSession();

        Assert.Equal(SessionValidationResult.Valid, await ValidateAsync());
    }

    [Fact]
    public async Task ValidateAsync_PendingVerificationUser_ReturnsValid()
    {
        // Only suspended and deactivated accounts are locked; a minor kept pending until
        // guardian consent must still be able to use the session to reach the consent screens.
        GivenSession(userStatus: UserStatus.PendingVerification);

        Assert.Equal(SessionValidationResult.Valid, await ValidateAsync());
    }

    [Fact]
    public async Task ValidateAsync_SupersededSession_ReturnsSuperseded()
    {
        GivenSession(revokedReason: SessionRevocationReason.SupersededByNewLogin);

        Assert.Equal(SessionValidationResult.Superseded, await ValidateAsync());
    }

    [Theory]
    [InlineData(SessionRevocationReason.LoggedOut)]
    [InlineData(SessionRevocationReason.AdminForced)]
    [InlineData(SessionRevocationReason.PasswordReset)]
    public async Task ValidateAsync_LoggedOutSession_ReturnsRevoked(SessionRevocationReason reason)
    {
        GivenSession(revokedReason: reason);

        Assert.Equal(SessionValidationResult.Revoked, await ValidateAsync());
    }

    [Fact]
    public async Task ValidateAsync_ExpiredSession_ReturnsExpired()
    {
        GivenSession(expiresAtUtc: Now.AddHours(1));
        _clock.UtcNow = Now.AddHours(1).AddTicks(1);

        Assert.Equal(SessionValidationResult.Expired, await ValidateAsync());
    }

    [Fact]
    public async Task ValidateAsync_AtTheExactExpiryInstant_ReturnsExpired()
    {
        // Mirrors UserSession.IsActive, which stops counting a session as active at its expiry.
        GivenSession(expiresAtUtc: Now.AddHours(1));
        _clock.UtcNow = Now.AddHours(1);

        Assert.Equal(SessionValidationResult.Expired, await ValidateAsync());
    }

    [Fact]
    public async Task ValidateAsync_SessionOfAnotherUser_ReturnsUnknown()
    {
        GivenSession(userId: Guid.NewGuid());

        Assert.Equal(SessionValidationResult.Unknown, await ValidateAsync());
    }

    [Fact]
    public async Task ValidateAsync_MissingSession_ReturnsUnknown()
    {
        _sessionLookup.FindAsync(_sessionId, Arg.Any<CancellationToken>()).Returns((SessionSnapshot?)null);

        Assert.Equal(SessionValidationResult.Unknown, await ValidateAsync());
    }

    [Theory]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Deactivated)]
    public async Task ValidateAsync_SuspendedUser_ReturnsAccountLocked(UserStatus status)
    {
        GivenSession(userStatus: status);

        Assert.Equal(SessionValidationResult.AccountLocked, await ValidateAsync());
    }

    [Fact]
    public async Task ValidateAsync_SuspendedUserWhoseSessionsWereRevoked_ReturnsAccountLocked()
    {
        // Suspending an account also revokes its sessions; the lock is the answer that tells
        // the user signing in again will not help.
        GivenSession(revokedReason: SessionRevocationReason.AccountSuspended, userStatus: UserStatus.Suspended);

        Assert.Equal(SessionValidationResult.AccountLocked, await ValidateAsync());
    }

    [Theory]
    [InlineData(SessionValidationResult.Unknown, "session_unknown")]
    [InlineData(SessionValidationResult.Superseded, "session_superseded")]
    [InlineData(SessionValidationResult.Revoked, "session_revoked")]
    [InlineData(SessionValidationResult.Expired, "session_expired")]
    [InlineData(SessionValidationResult.AccountLocked, "account_locked")]
    public void ToErrorCode_MapsEachRejectionToItsStableCode(SessionValidationResult result, string expectedCode)
    {
        Assert.Equal(expectedCode, result.ToErrorCode());
        Assert.False(string.IsNullOrWhiteSpace(result.ToMessage()));
    }

    [Fact]
    public void ToErrorCode_ForValid_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionValidationResult.Valid.ToErrorCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionValidationResult.Valid.ToMessage());
    }
}
