using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Events;
using ExamPlatform.Modules.Identity.Domain.Exceptions;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class UserTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static User CreateAdultUser() =>
        User.Register("candidate@example.com", null, new DateOnly(2000, 1, 1), "Test Candidate", Now);

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
