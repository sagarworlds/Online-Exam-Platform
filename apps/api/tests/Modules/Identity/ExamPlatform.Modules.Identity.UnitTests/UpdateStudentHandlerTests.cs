using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.Invite.Contracts;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>
/// Staff correcting a student's profile: what is checked before anything is touched, that invitations follow a new e-mail address before
/// the account is saved, that a changed address or number signs the student out, and that the audit entry names fields but never values.
/// </summary>
public class UpdateStudentHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IInviteAddressChanger _invites = Substitute.For<IInviteAddressChanger>();
    private readonly IOtpChallengeRepository _challenges = Substitute.For<IOtpChallengeRepository>();
    private readonly IPasswordResetTokenRepository _resetTokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly User _student = User.Register("asha@example.com", "+919800000000", new DateOnly(2000, 1, 1), "Asha Rao", Now);
    private readonly UserSession _session;
    private AuditEntry? _recorded;

    public UpdateStudentHandlerTests()
    {
        _student.AssignRole(Role.Create(RbacCatalog.RoleNames.Candidate, requiresTwoFactor: false));
        _session = _student.StartNewSession("session-hash", Now, Now.AddHours(1), null, null);
        _users.GetByIdAsync(_student.Id, Arg.Any<CancellationToken>()).Returns(_student);
        _invites.ChangeAddressAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(2);
        _challenges.GetOutstandingForUserAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
        _audit.RecordAsync(Arg.Do<AuditEntry>(entry => _recorded = entry), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    /// <summary>The student's profile as it is now, with any of it replaced.</summary>
    private UpdateStudentCommand Command(
        string? name = "Asha Rao",
        string? email = "asha@example.com",
        string? phone = "+919800000000",
        DateOnly? dateOfBirth = null,
        Guid? studentId = null) =>
        new(studentId ?? _student.Id, name, email, phone, dateOfBirth ?? new DateOnly(2000, 1, 1), StaffId, RbacCatalog.RoleNames.ExamAdmin);

    private Task<UpdateStudentResultDto> RunAsync(UpdateStudentCommand command) =>
        new UpdateStudentHandler(
                _users, _invites, new StudentSignInRevoker(_challenges, _resetTokens), _audit, _unitOfWork, new FakeClock(Now), Substitute.For<ILogger<UpdateStudentHandler>>())
            .HandleAsync(command, CancellationToken.None);

    private async Task AssertNothingHappenedAsync()
    {
        await _invites.DidNotReceiveWithAnyArgs().ChangeAddressAsync(default!, default!, default);
        await AssertNobodyWasSignedOutAsync();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    private async Task AssertNobodyWasSignedOutAsync()
    {
        await _challenges.DidNotReceiveWithAnyArgs().GetOutstandingForUserAsync(default, default, default);
        await _resetTokens.DidNotReceiveWithAnyArgs().RevokeOutstandingForUserAsync(default, default, default);
    }

    [Fact]
    public async Task HandleAsync_WithNothingChanged_SavesAndRecordsNothing()
    {
        var result = await RunAsync(Command());

        Assert.Empty(result.Changed);
        Assert.Equal(0, result.InvitationsMoved);
        Assert.False(result.SignedOut);
        Assert.True(_session.IsActive(Now));
        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task HandleAsync_ChangingOnlyTheName_KeepsTheStudentSignedIn()
    {
        var result = await RunAsync(Command(name: "  Asha R. Rao "));

        Assert.Equal(["name"], result.Changed);
        Assert.Equal("Asha R. Rao", result.Student.DisplayName);
        Assert.False(result.SignedOut);
        Assert.True(_session.IsActive(Now));
        await AssertNobodyWasSignedOutAsync();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _invites.DidNotReceiveWithAnyArgs().ChangeAddressAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_ChangingTheEmail_MovesTheInvitationsBeforeSavingTheAccount()
    {
        var result = await RunAsync(Command(email: "asha.rao@example.org"));

        Assert.Equal(["email"], result.Changed);
        Assert.Equal("asha.rao@example.org", result.Student.Email);
        Assert.Equal(2, result.InvitationsMoved);
        Received.InOrder(() =>
        {
            _invites.ChangeAddressAsync("asha@example.com", "asha.rao@example.org", Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task HandleAsync_ChangingTheEmail_SignsTheStudentOut()
    {
        var result = await RunAsync(Command(email: "asha.rao@example.org"));

        Assert.True(result.SignedOut);
        Assert.False(_session.IsActive(Now));
        Assert.Equal(SessionRevocationReason.ContactChanged, _session.RevokedReason);
    }

    [Theory]
    [InlineData("asha.rao@example.org", null)]
    [InlineData(null, "+919811111111")]
    public async Task HandleAsync_ChangingTheEmailOrNumber_EndsTheCodesAndResetLinksAlreadySentToTheOldOne(string? newEmail, string? newPhone)
    {
        // A code requested at the old address a moment before the change must not sign anyone in afterwards.
        var requested = OtpChallenge.Issue(_student.Id, OtpChannel.Email, "asha@example.com", "hash", OtpPurpose.Login, Now, TimeSpan.FromMinutes(10));
        _challenges.GetOutstandingForUserAsync(_student.Id, Now, Arg.Any<CancellationToken>()).Returns([requested]);

        await RunAsync(Command(email: newEmail ?? "asha@example.com", phone: newPhone ?? "+919800000000"));

        Assert.True(requested.IsSuperseded);
        await _resetTokens.Received(1).RevokeOutstandingForUserAsync(_student.Id, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ChangingTheMobileNumber_SignsOutButLeavesTheInvitationsAlone()
    {
        var result = await RunAsync(Command(phone: "+91 98111 11111"));

        Assert.Equal(["phoneNumber"], result.Changed);
        Assert.Equal("+91 98111 11111", result.Student.PhoneNumber);
        Assert.True(result.SignedOut);
        Assert.Equal(SessionRevocationReason.ContactChanged, _session.RevokedReason);
        Assert.Equal(0, result.InvitationsMoved);
        await _invites.DidNotReceiveWithAnyArgs().ChangeAddressAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_AddingAnEmailToAPhoneOnlyAccount_HasNoInvitationsToMove()
    {
        var phoneOnly = User.Register(null, "+919822222222", new DateOnly(2000, 1, 1), "Phone Only", Now);
        phoneOnly.AssignRole(Role.Create(RbacCatalog.RoleNames.Candidate, requiresTwoFactor: false));
        _users.GetByIdAsync(phoneOnly.Id, Arg.Any<CancellationToken>()).Returns(phoneOnly);

        var result = await RunAsync(Command(email: "new@example.com", phone: "+919822222222", name: "Phone Only", studentId: phoneOnly.Id));

        Assert.Equal(["email"], result.Changed);
        Assert.Equal(0, result.InvitationsMoved);
        Assert.True(result.SignedOut);
        await _invites.DidNotReceiveWithAnyArgs().ChangeAddressAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_RemovingTheEmail_KeepsThePhoneAndLeavesInvitationsWhereTheyAre()
    {
        var result = await RunAsync(Command(email: "  "));

        Assert.Equal(["email"], result.Changed);
        Assert.Null(result.Student.Email);
        Assert.True(result.SignedOut);
        await _invites.DidNotReceiveWithAnyArgs().ChangeAddressAsync(default!, default!, default);
    }

    [Fact]
    public async Task HandleAsync_ChangingTheDateOfBirthAcrossTheAdultLine_AuditsTheAgeBandsButNotTheDate()
    {
        var result = await RunAsync(Command(dateOfBirth: new DateOnly(2012, 5, 1)));

        Assert.Equal(["dateOfBirth"], result.Changed);
        Assert.Equal("Minor", result.Student.AgeBand);
        Assert.False(result.SignedOut);
        Assert.NotNull(_recorded);
        Assert.Equal("Adult", _recorded.Metadata["ageBandBefore"]);
        Assert.Equal("Minor", _recorded.Metadata["ageBandAfter"]);
        Assert.DoesNotContain(_recorded.Metadata.Values, value => value.Contains("2012") || value.Contains("2000"));
    }

    [Fact]
    public async Task HandleAsync_RecordsWhoChangedWhichFields_WithoutAnyPersonalValue()
    {
        await RunAsync(Command(name: "Asha Kumar", email: "asha.kumar@example.org", phone: "+919833333333"));

        Assert.NotNull(_recorded);
        Assert.Equal("Identity.StudentProfileChanged", _recorded.Action);
        Assert.Equal("User", _recorded.EntityType);
        Assert.Equal(_student.Id.ToString(), _recorded.EntityId);
        Assert.Equal(StaffId, _recorded.ActorUserId);
        Assert.Equal(RbacCatalog.RoleNames.ExamAdmin, _recorded.ActorRole);
        Assert.Equal("name,email,phoneNumber", _recorded.Metadata["fields"]);
        Assert.Equal("2", _recorded.Metadata["invitationsMoved"]);
        Assert.Equal("true", _recorded.Metadata["signedOut"]);
        // NFR-6: no name, address or number in the trail.
        Assert.DoesNotContain(_recorded.Metadata.Values, value => value.Contains("Kumar") || value.Contains("@") || value.Contains("+91"));
        Assert.False(_recorded.Metadata.ContainsKey("ageBandBefore"));
    }

    [Fact]
    public async Task HandleAsync_ForAnAccountThatDoesNotExist_ThrowsUserNotFoundError()
    {
        await Assert.ThrowsAsync<UserNotFoundError>(() => RunAsync(Command(studentId: Guid.NewGuid())));

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task HandleAsync_ForAStaffAccount_ThrowsTheSameNotFoundError()
    {
        var colleague = User.Register("teacher@example.com", null, new DateOnly(1985, 1, 1), "A Teacher", Now);
        colleague.AssignRole(Role.Create(RbacCatalog.RoleNames.Candidate, requiresTwoFactor: false));
        colleague.AssignRole(Role.Create(RbacCatalog.RoleNames.InstituteTeacher, requiresTwoFactor: false));
        _users.GetByIdAsync(colleague.Id, Arg.Any<CancellationToken>()).Returns(colleague);

        await Assert.ThrowsAsync<UserNotFoundError>(
            () => RunAsync(Command(email: "someone.else@example.com", studentId: colleague.Id)));

        Assert.Equal("teacher@example.com", colleague.Email);
        await AssertNothingHappenedAsync();
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("two@@example.com")]
    [InlineData("spaces in@example.com")]
    public async Task HandleAsync_WithAnInvalidEmail_ThrowsAndTouchesNothing(string email)
    {
        await Assert.ThrowsAsync<InvalidEmailAddressError>(() => RunAsync(Command(email: email)));

        await AssertNothingHappenedAsync();
        Assert.True(_session.IsActive(Now));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("+91-98000-0000x")]
    [InlineData("++919800000000")]
    public async Task HandleAsync_WithAnInvalidMobileNumber_ThrowsAndTouchesNothing(string phone)
    {
        await Assert.ThrowsAsync<InvalidPhoneNumberError>(() => RunAsync(Command(phone: phone)));

        await AssertNothingHappenedAsync();
        Assert.True(_session.IsActive(Now));
    }

    [Fact]
    public async Task HandleAsync_WithNeitherEmailNorNumber_ThrowsContactRequiredError()
    {
        await Assert.ThrowsAsync<ContactRequiredError>(() => RunAsync(Command(email: null, phone: " ")));

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task HandleAsync_WithABlankName_ThrowsInvalidDisplayNameError()
    {
        await Assert.ThrowsAsync<InvalidDisplayNameError>(() => RunAsync(Command(name: "   ")));

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task HandleAsync_WithoutADateOfBirth_ThrowsInvalidDateOfBirthError()
    {
        var command = Command() with { DateOfBirth = null };

        await Assert.ThrowsAsync<InvalidDateOfBirthError>(() => RunAsync(command));

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task HandleAsync_WithAFutureDateOfBirth_ThrowsInvalidDateOfBirthError()
    {
        await Assert.ThrowsAsync<InvalidDateOfBirthError>(() => RunAsync(Command(dateOfBirth: new DateOnly(2030, 1, 1))));

        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherAccountHoldsTheEmail_ThrowsContactInUseAndMovesNothing()
    {
        _users.AnyOtherWithEmailAsync("taken@example.com", _student.Id, Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<ContactInUseError>(() => RunAsync(Command(email: "taken@example.com")));

        Assert.Equal(ContactInUseError.Email().Message, error.Message);
        await AssertNothingHappenedAsync();
        Assert.True(_session.IsActive(Now));
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherAccountHoldsTheNumber_ThrowsContactInUseAndMovesNothing()
    {
        _users.AnyOtherWithPhoneAsync("+919844444444", _student.Id, Arg.Any<CancellationToken>()).Returns(true);

        var error = await Assert.ThrowsAsync<ContactInUseError>(() => RunAsync(Command(phone: "+919844444444")));

        Assert.Equal(ContactInUseError.Phone().Message, error.Message);
        await AssertNothingHappenedAsync();
    }

    [Fact]
    public async Task HandleAsync_KeepingTheSameEmail_DoesNotAskWhetherItIsTaken()
    {
        // The student's own address is held by the student: asking would find them and refuse a change of name.
        await RunAsync(Command(name: "Asha Kumar"));

        await _users.DidNotReceiveWithAnyArgs().AnyOtherWithEmailAsync(default!, default, default);
        await _users.DidNotReceiveWithAnyArgs().AnyOtherWithPhoneAsync(default!, default, default);
    }

    [Fact]
    public async Task HandleAsync_WhenTheSaveFails_TheErrorPropagatesAndNothingIsAudited()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new InvalidOperationException("database down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(Command(email: "asha.rao@example.org")));

        // The invitations have already moved; sending the same change again finishes the job, so the audit entry waits for the save.
        await _invites.Received(1).ChangeAddressAsync("asha@example.com", "asha.rao@example.org", Arg.Any<CancellationToken>());
        await _audit.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }
}
