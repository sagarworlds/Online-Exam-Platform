using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>
/// Staff turning a deactivated student back on: the account becomes active, only a deactivated account changes (a suspended one stays
/// suspended), the sessions the deactivation ended stay ended, the audit entry names who did it and no personal data, a staff account
/// cannot be reached this way, and doing it twice changes nothing.
/// </summary>
public class ReactivateStudentHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly User _student = User.Register("asha@example.com", "+919800000000", new DateOnly(2000, 1, 1), "Asha Rao", Now);
    private readonly UserSession _session;
    private AuditEntry? _recorded;

    public ReactivateStudentHandlerTests()
    {
        _student.AssignRole(Role.Create(RbacCatalog.RoleNames.Candidate, requiresTwoFactor: false));
        _student.Activate();
        _session = _student.StartNewSession("session-hash", Now, Now.AddHours(1), null, null);
        _users.GetByIdAsync(_student.Id, Arg.Any<CancellationToken>()).Returns(_student);
        _audit.RecordAsync(Arg.Do<AuditEntry>(entry => _recorded = entry), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private Task<StudentDto> RunAsync(Guid? studentId = null) =>
        new ReactivateStudentHandler(_users, _audit, _unitOfWork, new FakeClock(Now), Substitute.For<ILogger<ReactivateStudentHandler>>())
            .HandleAsync(new ReactivateStudentCommand(studentId ?? _student.Id, StaffId, RbacCatalog.RoleNames.ExamAdmin), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_TurnsADeactivatedAccountBackOn()
    {
        _student.Deactivate(Now.AddDays(-1));

        var result = await RunAsync();

        Assert.Equal("Active", result.Status);
        Assert.Equal(UserStatus.Active, _student.Status);
        Assert.Equal("Asha Rao", result.DisplayName);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DoesNotBringBackTheSessionsTheDeactivationEnded()
    {
        _student.Deactivate(Now.AddDays(-1));

        await RunAsync();

        Assert.False(_session.IsActive(Now));
        Assert.Equal(SessionRevocationReason.AccountDeactivated, _session.RevokedReason);
    }

    [Fact]
    public async Task HandleAsync_RecordsWhoDidIt_WithoutAnyPersonalData()
    {
        _student.Deactivate(Now.AddDays(-1));

        await RunAsync();

        Assert.NotNull(_recorded);
        Assert.Equal("Identity.StudentReactivated", _recorded!.Action);
        Assert.Equal(StaffId, _recorded.ActorUserId);
        Assert.Equal(RbacCatalog.RoleNames.ExamAdmin, _recorded.ActorRole);
        Assert.Equal(_student.Id.ToString(), _recorded.EntityId);
        Assert.Empty(_recorded.Metadata);
    }

    [Fact]
    public async Task HandleAsync_OnAnActiveAccount_ChangesNothing()
    {
        var result = await RunAsync();

        Assert.Equal("Active", result.Status);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_OnASuspendedAccount_LeavesItSuspended()
    {
        _student.Suspend(Now.AddDays(-1));

        var result = await RunAsync();

        Assert.Equal("Suspended", result.Status);
        Assert.Equal(UserStatus.Suspended, _student.Status);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task HandleAsync_Twice_ChangesNothingTheSecondTime_AndIsAuditedOnce()
    {
        _student.Deactivate(Now.AddDays(-1));

        await RunAsync();
        await RunAsync();

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.Received(1).RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ForAStaffAccount_IsNotFound_AndTouchesNothing()
    {
        var staff = User.Register("admin@example.com", null, new DateOnly(1985, 5, 5), "Admin", Now);
        staff.AssignRole(Role.Create(RbacCatalog.RoleNames.ExamAdmin, requiresTwoFactor: true));
        staff.Deactivate(Now.AddDays(-1));
        _users.GetByIdAsync(staff.Id, Arg.Any<CancellationToken>()).Returns(staff);

        await Assert.ThrowsAsync<UserNotFoundError>(() => RunAsync(staff.Id));

        Assert.Equal(UserStatus.Deactivated, staff.Status);
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
    public void Reactivate_OnlyChangesADeactivatedAccount()
    {
        Assert.False(_student.Reactivate());

        _student.Deactivate(Now);
        Assert.True(_student.Reactivate());
        Assert.Equal(UserStatus.Active, _student.Status);
        Assert.False(_student.Reactivate());
    }
}
