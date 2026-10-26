using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>
/// Signing a student in from a link that was e-mailed to them. The link stands in for a one-time code, so it is for students only, and
/// it must leave a refused account exactly as it found it.
/// </summary>
public class StudentDirectSignInTests
{
    private const string Email = "Priya.Sharma@Example.com";

    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);

    private readonly Role _candidateRole = Role.Create(RbacCatalog.RoleNames.Candidate, requiresTwoFactor: false);
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly ITokenGenerator _tokens = Substitute.For<ITokenGenerator>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly StudentDirectSignIn _signIn;

    public StudentDirectSignInTests()
    {
        _roles.GetByNameAsync(RbacCatalog.RoleNames.Candidate, Arg.Any<CancellationToken>()).Returns(_candidateRole);
        _users.ListByEmailIgnoreCaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        _tokens.GenerateAccessToken(Arg.Any<User>(), Arg.Any<UserSession>()).Returns("access-token");

        var clock = new FakeClock(Now);
        _signIn = new StudentDirectSignIn(
            _users, _roles, new LoginEligibilityPolicy(), new LoginSessionIssuer(_tokens, clock), _audit, _unitOfWork, clock);
    }

    private User Student(string email = Email, bool activate = true)
    {
        var user = User.Register(email, null, new DateOnly(2005, 5, 5), "Priya", Now.AddDays(-30));
        user.AssignRole(_candidateRole);
        if (activate)
            user.Activate();
        return user;
    }

    private void Holders(params User[] users) =>
        _users.ListByEmailIgnoreCaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(users);

    // ---- finding or making the account -----------------------------------------------------------

    [Fact]
    public async Task FindOrCreate_ForAnExistingStudent_ReturnsTheirAccount_AndMakesNoneNew()
    {
        var student = Student();
        Holders(student);

        var account = await _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None);

        Assert.Equal(student.Id, account.UserId);
        Assert.False(account.Created);
        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task FindOrCreate_LooksTheAddressUpWithoutRegardToCase_AndTrimmed()
    {
        Holders(Student());

        await _signIn.FindOrCreateStudentAsync("  priya.sharma@example.com ", CancellationToken.None);

        await _users.Received(1).ListByEmailIgnoreCaseAsync("priya.sharma@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindOrCreate_WhenNobodyHoldsTheAddress_MakesAnActiveStudent_WithAnAdultPlaceholderDateOfBirth()
    {
        User? added = null;
        await _users.AddAsync(Arg.Do<User>(user => added = user), Arg.Any<CancellationToken>());

        var account = await _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None);

        Assert.True(account.Created);
        Assert.NotNull(added);
        Assert.Equal(added.Id, account.UserId);
        Assert.Equal(Email, added.Email);
        Assert.Null(added.PhoneNumber);
        Assert.Equal(UserStatus.Active, added.Status);
        Assert.True(added.IsStudent);
        Assert.Equal(User.PlaceholderAdultDateOfBirth, added.DateOfBirth);
        Assert.Equal("Priya.Sharma", added.DisplayName);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("a@example.com", "a")]
    [InlineData("first.last+exam@example.com", "first.last+exam")]
    public async Task FindOrCreate_NamesTheNewStudentAfterTheLocalPartOfTheAddress(string email, string expectedName)
    {
        User? added = null;
        await _users.AddAsync(Arg.Do<User>(user => added = user), Arg.Any<CancellationToken>());

        await _signIn.FindOrCreateStudentAsync(email, CancellationToken.None);

        Assert.Equal(expectedName, added!.DisplayName);
    }

    [Fact]
    public async Task FindOrCreate_ForANewStudent_AuditsItByIdOnly_NeverByAddress()
    {
        AuditEntry? entry = null;
        await _audit.RecordAsync(Arg.Do<AuditEntry>(e => entry = e), Arg.Any<CancellationToken>());

        var account = await _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("Identity.StudentAccountCreatedFromInviteLink", entry.Action);
        Assert.Equal(account.UserId, entry.ActorUserId);
        Assert.Equal(account.UserId.ToString(), entry.EntityId);
        Assert.DoesNotContain(Email, string.Concat(entry.Metadata.Values), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FindOrCreate_WhenTheCandidateRoleIsNotSeeded_Throws_AndCreatesNothing()
    {
        _roles.GetByNameAsync(RbacCatalog.RoleNames.Candidate, Arg.Any<CancellationToken>()).Returns((Role?)null);

        await Assert.ThrowsAsync<RoleNotFoundError>(() => _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None));

        await _users.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    // ---- accounts that must not be signed in this way --------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindOrCreate_ForALockedAccount_ThrowsAccountLocked_AndChangesNothing(bool suspended)
    {
        var student = Student();
        if (suspended)
            student.Suspend(Now);
        else
            student.Deactivate(Now);
        Holders(student);

        var error = await Assert.ThrowsAsync<AccountLockedError>(() => _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None));

        Assert.Equal(403, error.HttpStatusCode);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Theory]
    [InlineData(RbacCatalog.RoleNames.SuperAdmin, true)]
    [InlineData(RbacCatalog.RoleNames.ExamAdmin, true)]
    [InlineData(RbacCatalog.RoleNames.InstituteTeacher, false)]
    public async Task FindOrCreate_ForAStaffAccount_ThrowsNotAllowed_EvenWhenItNeedsNoSecondFactor(string roleName, bool twoFactor)
    {
        // A teacher needs no second factor yet holds staff powers (invite.manage, student.read): "no 2FA" is not the test.
        var staff = Student();
        staff.AssignRole(Role.Create(roleName, twoFactor));
        Holders(staff);

        var error = await Assert.ThrowsAsync<DirectSignInNotAllowedError>(() => _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None));

        Assert.Equal(403, error.HttpStatusCode);
        Assert.Equal("direct_sign_in_not_allowed", error.ErrorCode);
    }

    [Fact]
    public async Task FindOrCreate_WhenTwoAccountsDifferOnlyByCase_ThrowsNotAllowed_BecauseItIsNotClearWhoseTheLinkIs()
    {
        Holders(Student("priya.sharma@example.com"), Student("Priya.Sharma@example.com"));

        await Assert.ThrowsAsync<DirectSignInNotAllowedError>(() => _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task FindOrCreate_ForAPendingAccount_ReturnsIt_WithoutActivatingYet()
    {
        var pending = Student(activate: false);
        Holders(pending);

        var account = await _signIn.FindOrCreateStudentAsync(Email, CancellationToken.None);

        // Activation belongs with the session, after the invite's code is spent; finding the account changes nothing.
        Assert.Equal(pending.Id, account.UserId);
        Assert.Equal(UserStatus.PendingVerification, pending.Status);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // ---- starting the session --------------------------------------------------------------------

    [Fact]
    public async Task StartSession_IssuesATokenAndASession_RecordingWhereItWasOpenedFrom_AndSavesOnce()
    {
        var student = Student();
        _users.GetByIdAsync(student.Id, Arg.Any<CancellationToken>()).Returns(student);

        var session = await _signIn.StartSessionAsync(student.Id, "device-1", "203.0.113.7", CancellationToken.None);

        Assert.Equal("access-token", session.AccessToken);
        var stored = Assert.Single(student.Sessions);
        Assert.Equal(stored.Id, session.SessionId);
        Assert.Equal("device-1", stored.DeviceFingerprint);
        Assert.Equal("203.0.113.7", stored.IpAddress);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartSession_ForAPendingAccount_ActivatesIt_BecauseTheLinkProvedTheAddress()
    {
        var pending = Student(activate: false);
        _users.GetByIdAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(pending);

        await _signIn.StartSessionAsync(pending.Id, null, null, CancellationToken.None);

        Assert.Equal(UserStatus.Active, pending.Status);
    }

    [Fact]
    public async Task StartSession_EndsTheStudentsOtherSession_AsEverySignInDoes()
    {
        var student = Student();
        _users.GetByIdAsync(student.Id, Arg.Any<CancellationToken>()).Returns(student);
        await _signIn.StartSessionAsync(student.Id, "phone", null, CancellationToken.None);
        var first = student.Sessions.Single();

        await _signIn.StartSessionAsync(student.Id, "laptop", null, CancellationToken.None);

        Assert.Equal(SessionRevocationReason.SupersededByNewLogin, first.RevokedReason);
    }

    [Fact]
    public async Task StartSession_ForAnAccountSuspendedSinceItWasFound_ThrowsAccountLocked_AndIssuesNothing()
    {
        var student = Student();
        student.Suspend(Now);
        _users.GetByIdAsync(student.Id, Arg.Any<CancellationToken>()).Returns(student);

        await Assert.ThrowsAsync<AccountLockedError>(() => _signIn.StartSessionAsync(student.Id, null, null, CancellationToken.None));

        _tokens.DidNotReceiveWithAnyArgs().GenerateAccessToken(default!, default!);
    }

    [Fact]
    public async Task StartSession_ForAnAccountThatIsNoLongerAStudent_ThrowsNotAllowed()
    {
        var student = Student();
        student.AssignRole(Role.Create(RbacCatalog.RoleNames.InstituteTeacher, requiresTwoFactor: false));
        _users.GetByIdAsync(student.Id, Arg.Any<CancellationToken>()).Returns(student);

        await Assert.ThrowsAsync<DirectSignInNotAllowedError>(() => _signIn.StartSessionAsync(student.Id, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task StartSession_ForAnUnknownAccount_Throws()
    {
        _users.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await Assert.ThrowsAnyAsync<Exception>(() => _signIn.StartSessionAsync(Guid.NewGuid(), null, null, CancellationToken.None));
    }
}
