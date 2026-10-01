using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Events;
using ExamPlatform.Modules.Identity.Domain.Exceptions;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class UserTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly UtcToday = DateOnly.FromDateTime(Now);

    private static User CreateAdultUser() => RegisterBornOn(new DateOnly(2000, 1, 1));

    private static User RegisterBornOn(DateOnly dateOfBirth) =>
        User.Register("candidate@example.com", null, dateOfBirth, "Test Candidate", Now);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    public void Register_WithoutEmailOrPhone_ThrowsContactRequiredError(string? email, string? phoneNumber)
    {
        var act = () => User.Register(email, phoneNumber, new DateOnly(2000, 1, 1), "Test Candidate", Now);

        Assert.Throws<ContactRequiredError>(act);
    }

    [Fact]
    public void Register_WithBlankEmailAndAPhone_StoresNoEmail()
    {
        var user = User.Register("  ", "+919800000000", new DateOnly(2000, 1, 1), "Test Candidate", Now);

        // Null, not blank, so the account does not collide on the unique email index.
        Assert.Null(user.Email);
        Assert.Equal("+919800000000", user.PhoneNumber);
    }

    [Theory]
    [InlineData(User.MaxEmailLength + 1, 0)]
    [InlineData(0, User.MaxPhoneNumberLength + 1)]
    public void Register_WithContactLongerThanStored_ThrowsInvalidContactError(int emailLength, int phoneLength)
    {
        var email = emailLength == 0 ? null : new string('a', emailLength - "@x.in".Length) + "@x.in";
        var phoneNumber = phoneLength == 0 ? null : new string('9', phoneLength);

        var act = () => User.Register(email, phoneNumber, new DateOnly(2000, 1, 1), "Test Candidate", Now);

        Assert.Throws<InvalidContactError>(act);
    }

    [Fact]
    public void Register_WithContactsAtTheStoredMaximum_IsAccepted()
    {
        var email = new string('a', User.MaxEmailLength - "@x.in".Length) + "@x.in";
        var phoneNumber = new string('9', User.MaxPhoneNumberLength);

        var user = User.Register(email, phoneNumber, new DateOnly(2000, 1, 1), "Test Candidate", Now);

        Assert.Equal(User.MaxEmailLength, user.Email!.Length);
        Assert.Equal(User.MaxPhoneNumberLength, user.PhoneNumber!.Length);
    }

    [Fact]
    public void Register_WithDefaultDateOfBirth_ThrowsInvalidDateOfBirthError()
    {
        // 0001-01-01 is what an omitted date used to bind to, and it reads as an adult.
        var error = Assert.Throws<InvalidDateOfBirthError>(() => RegisterBornOn(default));

        Assert.Equal("invalid_date_of_birth", error.ErrorCode);
        Assert.Equal(InvalidDateOfBirthError.Missing().Message, error.Message);
    }

    [Fact]
    public void Register_WithFutureDateOfBirth_ThrowsInvalidDateOfBirthError()
    {
        var error = Assert.Throws<InvalidDateOfBirthError>(() => RegisterBornOn(UtcToday.AddDays(2)));

        Assert.Equal(InvalidDateOfBirthError.InTheFuture().Message, error.Message);
    }

    [Fact]
    public void Register_WithDateOfBirthOneDayAheadOfUtc_IsAccepted()
    {
        // Born "today" in India while it is still yesterday by the UTC calendar.
        var user = RegisterBornOn(UtcToday.AddDays(1));

        Assert.Equal(UtcToday.AddDays(1), user.DateOfBirth);
    }

    [Fact]
    public void Register_OlderThan120Years_ThrowsInvalidDateOfBirthError()
    {
        var dateOfBirth = UtcToday.AddYears(-User.MaximumPlausibleAgeYears).AddDays(-1);

        var error = Assert.Throws<InvalidDateOfBirthError>(() => RegisterBornOn(dateOfBirth));

        Assert.Equal(InvalidDateOfBirthError.TooLongAgo().Message, error.Message);
    }

    [Fact]
    public void Register_Exactly120YearsAgo_IsAccepted()
    {
        var dateOfBirth = UtcToday.AddYears(-User.MaximumPlausibleAgeYears);

        Assert.Equal(dateOfBirth, RegisterBornOn(dateOfBirth).DateOfBirth);
    }

    [Fact]
    public void StartNewSession_WhenPriorActiveSessionExists_SupersedesPriorSessionAndEmitsEvent()
    {
        var user = CreateAdultUser();
        var firstSession = user.StartNewSession("hash-1", Now, Now.AddHours(1), null, null);

        var secondSession = user.StartNewSession("hash-2", Now.AddMinutes(5), Now.AddHours(1), null, null);

        Assert.False(firstSession.IsActive(Now.AddMinutes(5)));
        Assert.Equal(SessionRevocationReason.SupersededByNewLogin, firstSession.RevokedReason);
        Assert.True(secondSession.IsActive(Now.AddMinutes(5)));

        var supersededEvent = Assert.Single(user.DomainEvents.OfType<SessionSupersededEvent>());
        Assert.Equal(firstSession.Id, supersededEvent.SupersededSessionId);
        Assert.Equal(secondSession.Id, supersededEvent.NewSessionId);
    }

    [Fact]
    public void StartNewSession_WhenPriorActiveSessionExistsAndSupersedeNotAllowed_ThrowsDuplicateSessionError()
    {
        var user = CreateAdultUser();
        user.StartNewSession("hash-1", Now, Now.AddHours(1), null, null);

        var act = () => user.StartNewSession("hash-2", Now.AddMinutes(5), Now.AddHours(1), null, null, allowSupersede: false);

        Assert.Throws<DuplicateSessionError>(act);
    }

    [Fact]
    public void Suspend_SetsStatusAndRevokesActiveSessions()
    {
        var user = CreateAdultUser();
        user.Activate();
        var session = user.StartNewSession("hash-1", Now, Now.AddHours(1), null, null);

        user.Suspend(Now.AddMinutes(5));

        Assert.Equal(UserStatus.Suspended, user.Status);
        Assert.False(session.IsActive(Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(5), session.RevokedAtUtc);
        Assert.Equal(SessionRevocationReason.AccountSuspended, session.RevokedReason);
    }

    [Fact]
    public void RevokeAllSessions_RevokesOnlyActiveSessions()
    {
        var user = CreateAdultUser();
        var superseded = user.StartNewSession("hash-1", Now, Now.AddHours(1), null, null);
        var expired = user.StartNewSession("hash-2", Now.AddMinutes(1), Now.AddMinutes(2), null, null);
        var active = user.StartNewSession("hash-3", Now.AddMinutes(3), Now.AddHours(1), null, null);
        var revokeAt = Now.AddMinutes(10);

        user.RevokeAllSessions(revokeAt, SessionRevocationReason.LoggedOut);

        // The already-revoked session keeps its original reason and time, the expired one is
        // left untouched, and only the session that was still live is revoked now.
        Assert.Equal(SessionRevocationReason.SupersededByNewLogin, superseded.RevokedReason);
        Assert.Equal(Now.AddMinutes(1), superseded.RevokedAtUtc);
        Assert.Null(expired.RevokedAtUtc);
        Assert.Equal(SessionRevocationReason.LoggedOut, active.RevokedReason);
        Assert.Equal(revokeAt, active.RevokedAtUtc);
    }

    [Fact]
    public void RequiresTwoFactor_WithRoleThatRequiresIt_ReturnsTrue()
    {
        var user = CreateAdultUser();
        user.AssignRole(Role.Create("SuperAdmin", requiresTwoFactor: true));

        Assert.True(user.RequiresTwoFactor);
    }

    [Fact]
    public void RequiresTwoFactor_WithOnlyCandidateRole_ReturnsFalse()
    {
        var user = CreateAdultUser();
        user.AssignRole(Role.Create("Candidate", requiresTwoFactor: false));

        Assert.False(user.RequiresTwoFactor);
    }

    [Fact]
    public void GetAgeBand_ForSeventeenYearOld_ReturnsMinor()
    {
        var dateOfBirth = new DateOnly(2009, 6, 1);
        var user = User.Register("minor@example.com", null, dateOfBirth, "Minor Candidate", Now);

        // As of 2026-06-01 this candidate turns 17, one day before is still 16.
        var asOf = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(AgeBand.Minor, user.GetAgeBand(asOf));
    }

    [Fact]
    public void GetAgeBand_ForEighteenYearOld_ReturnsAdult()
    {
        var dateOfBirth = new DateOnly(2008, 6, 1);
        var asOf = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = User.Register("adult@example.com", null, dateOfBirth, "Adult Candidate", Now);

        Assert.Equal(AgeBand.Adult, user.GetAgeBand(asOf));
    }
}
