using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>
/// Staff deactivating a student: the account is closed to sign-in and signed out, nothing is deleted, the audit entry names who did it and
/// no personal data, a staff account cannot be reached this way, and doing it twice changes nothing.
/// </summary>
public class DeactivateStudentHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IOtpChallengeRepository _challenges = Substitute.For<IOtpChallengeRepository>();
    private readonly IPasswordResetTokenRepository _resetTokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly User _student = User.Register("asha@example.com", "+919800000000", new DateOnly(2000, 1, 1), "Asha Rao", Now);
    private readonly UserSession _session;
    private AuditEntry? _recorded;

    public DeactivateStudentHandlerTests()
    {
        _student.AssignRole(Role.Create(RbacCatalog.RoleNames.Candidate, requiresTwoFactor: false));
        _student.Activate();
        _session = _student.StartNewSession("session-hash", Now, Now.AddHours(1), null, null);
        _users.GetByIdAsync(_student.Id, Arg.Any<CancellationToken>()).Returns(_student);
        _challenges.GetOutstandingForUserAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        _audit.RecordAsync(Arg.Do<AuditEntry>(entry => _recorded = entry), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private Task<ExamPlatform.Modules.Identity.Application.Dtos.StudentDto> RunAsync(Guid? studentId = null) =>
        new DeactivateStudentHandler(
                _users, new StudentSignInRevoker(_challenges, _resetTokens), _audit, _unitOfWork, new FakeClock(Now), Substitute.For<ILogger<DeactivateStudentHandler>>())
            .HandleAsync(new DeactivateStudentCommand(studentId ?? _student.Id, StaffId, RbacCatalog.RoleNames.ExamAdmin), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_DeactivatesTheAccount_AndKeepsIt()
    {
        var result = await RunAsync();

        Assert.Equal("Deactivated", result.Status);
        Assert.Equal(UserStatus.Deactivated, _student.Status);
        Assert.Equal("Asha Rao", result.DisplayName);
        Assert.Equal("asha@example.com", result.Email);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SignsTheStudentOut_AndEndsUnusedCodesAndLinks()
    {
        await RunAsync();

        Assert.False(_session.IsActive(Now));
        Assert.Equal(SessionRevocationReason.AccountDeactivated, _session.RevokedReason);
        await _challenges.Received(1).GetOutstandingForUserAsync(_student.Id, Now, Arg.Any<CancellationToken>());
        await _resetTokens.Received(1).RevokeOutstandingForUserAsync(_student.Id, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RecordsWhoDidIt_WithoutAnyPersonalData()
    {
        await RunAsync();

        Assert.NotNull(_recorded);
        Assert.Equal("Identity.StudentDeactivated", _recorded!.Action);
        Assert.Equal(StaffId, _recorded.ActorUserId);
        Assert.Equal(RbacCatalog.RoleNames.ExamAdmin, _recorded.ActorRole);
        Assert.Equal(_student.Id.ToString(), _recorded.EntityId);
        Assert.Empty(_recorded.Metadata);
    }

    [Fact]
    public async Task HandleAsync_OnAnAlreadyDeactivatedAccount_ChangesNothing()
    {
        _student.Deactivate(Now.AddDays(-1));

        var result = await RunAsync();

        Assert.Equal("Deactivated", result.Status);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        await _challenges.DidNotReceiveWithAnyArgs().GetOutstandingForUserAsync(default, default, default);
    }

    [Fact]
    public async Task HandleAsync_ForAStaffAccount_IsNotFound_AndTouchesNothing()
    {
        var staff = User.Register("admin@example.com", null, new DateOnly(1985, 5, 5), "Admin", Now);
        staff.AssignRole(Role.Create(RbacCatalog.RoleNames.ExamAdmin, requiresTwoFactor: true));
        _users.GetByIdAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);

        await Assert.ThrowsAsync<UserNotFoundError>(() => RunAsync(staff.Id));

        Assert.NotEqual(UserStatus.Deactivated, staff.Status);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_ForNoSuchAccount_IsNotFound()
    {
        await Assert.ThrowsAsync<UserNotFoundError>(() => RunAsync(Guid.NewGuid()));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public void Deactivate_IsRepeatable_AndKeepsTheFirstRevocation()
    {
        Assert.True(_student.Deactivate(Now));
        Assert.False(_student.Deactivate(Now.AddHours(2)));

        Assert.Equal(Now, _session.RevokedAtUtc);
    }
}
