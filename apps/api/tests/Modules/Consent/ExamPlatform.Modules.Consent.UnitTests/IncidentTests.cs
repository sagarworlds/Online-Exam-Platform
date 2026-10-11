using ExamPlatform.Modules.Consent.Domain;
using ExamPlatform.Modules.Consent.Domain.Exceptions;

namespace ExamPlatform.Modules.Consent.UnitTests;

public class IncidentTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Logger = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static Incident LogDetectedAt(DateTime detectedAtUtc, DateTime? nowUtc = null) =>
        Incident.Log("A staff laptop was lost", detectedAtUtc, IncidentCategory.DataBreach, "Candidate names", Logger, nowUtc ?? Now);

    // Walks an incident to the requested status by moves the lifecycle allows, so each test starts from a real history.
    private static Incident IncidentIn(IncidentStatus status)
    {
        var incident = LogDetectedAt(Now.AddHours(-1));
        switch (status)
        {
            case IncidentStatus.Reported:
                incident.ChangeStatus(IncidentStatus.Reported, "Reported to CERT-In", Reviewer, Now);
                break;
            case IncidentStatus.Closed:
                incident.ChangeStatus(IncidentStatus.Closed, "Closed as a false alarm", Reviewer, Now);
                break;
        }

        return incident;
    }

    // ---- logging and the due time -----------------------------------------------------------------

    [Fact]
    public void Log_StartsLoggedAndDueSixHoursAfterDetection()
    {
        var detected = Now.AddHours(-2);

        var incident = LogDetectedAt(detected);

        Assert.Equal(IncidentStatus.Logged, incident.Status);
        Assert.Equal(detected.AddHours(6), incident.EscalationDueAtUtc);
        Assert.Equal(Now, incident.LoggedAtUtc);
        Assert.Equal(Logger, incident.LoggedById);
        Assert.Empty(incident.StatusChanges);
    }

    [Fact]
    public void Window_IsSixHours()
    {
        Assert.Equal(TimeSpan.FromHours(6), IncidentEscalation.Window);
    }

    [Fact]
    public void Log_DueTimeComesFromDetection_SoAnIncidentLoggedLateIsOverdueOnArrival()
    {
        var detected = Now.AddHours(-10);

        var incident = LogDetectedAt(detected);

        Assert.Equal(detected.AddHours(6), incident.EscalationDueAtUtc);
        Assert.True(incident.IsOverdueAt(Now));
    }

    [Fact]
    public void Log_TrimsTheTextItKeeps()
    {
        var incident = Incident.Log("  Lost laptop  ", Now.AddHours(-1), IncidentCategory.Other, "  Names  ", Logger, Now);

        Assert.Equal("Lost laptop", incident.Description);
        Assert.Equal("Names", incident.AffectedData);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Log_RefusesBlankDescription(string? description)
    {
        var error = Assert.Throws<InvalidIncidentError>(() =>
            Incident.Log(description!, Now.AddHours(-1), IncidentCategory.Other, "Names", Logger, Now));

        Assert.Equal(400, error.HttpStatusCode);
        Assert.Equal("invalid_incident", error.ErrorCode);
    }

    [Fact]
    public void Log_AcceptsDescriptionAtTheLimit_AndRefusesOneCharacterMore()
    {
        var atLimit = new string('d', Incident.MaxDescriptionLength);
        var overLimit = new string('d', Incident.MaxDescriptionLength + 1);

        Assert.Equal(atLimit, Incident.Log(atLimit, Now.AddHours(-1), IncidentCategory.Other, "Names", Logger, Now).Description);
        Assert.Throws<InvalidIncidentError>(() =>
            Incident.Log(overLimit, Now.AddHours(-1), IncidentCategory.Other, "Names", Logger, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Log_RefusesBlankAffectedData(string? affectedData)
    {
        Assert.Throws<InvalidIncidentError>(() =>
            Incident.Log("A breach", Now.AddHours(-1), IncidentCategory.DataBreach, affectedData!, Logger, Now));
    }

    [Fact]
    public void Log_RefusesADetectionTimeInTheFuture_AndAcceptsOneExactlyNow()
    {
        Assert.Throws<InvalidIncidentError>(() =>
            Incident.Log("A breach", Now.AddSeconds(1), IncidentCategory.DataBreach, "Names", Logger, Now));

        var atNow = Incident.Log("A breach", Now, IncidentCategory.DataBreach, "Names", Logger, Now);
        Assert.Equal(Now.AddHours(6), atNow.EscalationDueAtUtc);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Log_RefusesADetectionTimeThatIsNotUtc(DateTimeKind kind)
    {
        var detected = DateTime.SpecifyKind(Now.AddHours(-1), kind);

        Assert.Throws<InvalidIncidentError>(() =>
            Incident.Log("A breach", detected, IncidentCategory.DataBreach, "Names", Logger, Now));
    }

    [Fact]
    public void Log_RefusesAnUnknownCategory()
    {
        Assert.Throws<InvalidIncidentError>(() =>
            Incident.Log("A breach", Now.AddHours(-1), (IncidentCategory)99, "Names", Logger, Now));
    }

    // ---- the overdue flag -------------------------------------------------------------------------

    [Fact]
    public void IsOverdue_IsFalseAtTheDueInstant_AndTrueOneTickAfter()
    {
        var due = Now;

        Assert.False(IncidentEscalation.IsOverdue(IncidentStatus.Logged, due, due));
        Assert.True(IncidentEscalation.IsOverdue(IncidentStatus.Logged, due, due.AddTicks(1)));
    }

    [Fact]
    public void IsOverdue_IsFalseBeforeTheDueTime()
    {
        var incident = LogDetectedAt(Now.AddHours(-1));

        Assert.False(incident.IsOverdueAt(Now.AddHours(4)));
    }

    [Fact]
    public void IsOverdue_BecomesTrueOnceLoggedPastDueTime()
    {
        var incident = LogDetectedAt(Now.AddHours(-1));

        Assert.True(incident.IsOverdueAt(Now.AddHours(5).AddTicks(1)));
    }

    [Fact]
    public void IsOverdue_IsFalseOnceReported_EvenPastTheDueTime()
    {
        var incident = LogDetectedAt(Now.AddHours(-10));
        incident.ChangeStatus(IncidentStatus.Reported, "Reported to CERT-In", Reviewer, Now);

        Assert.False(incident.IsOverdueAt(Now.AddDays(1)));
    }

    [Fact]
    public void IsOverdue_IsFalseWhenClosed_EvenPastTheDueTime()
    {
        var incident = LogDetectedAt(Now.AddHours(-10));
        incident.ChangeStatus(IncidentStatus.Closed, "Closed as a false alarm", Reviewer, Now);

        Assert.False(incident.IsOverdueAt(Now.AddDays(1)));
    }

    // ---- status changes ---------------------------------------------------------------------------

    [Fact]
    public void ChangeStatus_LoggedToReported_RecordsWhoWhenAndTheTrimmedNote()
    {
        var incident = LogDetectedAt(Now.AddHours(-1));
        var later = Now.AddHours(1);

        incident.ChangeStatus(IncidentStatus.Reported, "  Reported to CERT-In by e-mail  ", Reviewer, later);

        Assert.Equal(IncidentStatus.Reported, incident.Status);
        var change = Assert.Single(incident.StatusChanges);
        Assert.Equal(IncidentStatus.Logged, change.FromStatus);
        Assert.Equal(IncidentStatus.Reported, change.ToStatus);
        Assert.Equal("Reported to CERT-In by e-mail", change.Note);
        Assert.Equal(Reviewer, change.ChangedById);
        Assert.Equal(later, change.ChangedAtUtc);
    }

    [Theory]
    [InlineData(IncidentStatus.Logged, IncidentStatus.Reported)]
    [InlineData(IncidentStatus.Logged, IncidentStatus.Closed)]
    [InlineData(IncidentStatus.Reported, IncidentStatus.Closed)]
    public void ChangeStatus_AllowsTheForwardMoves(IncidentStatus from, IncidentStatus to)
    {
        var incident = IncidentIn(from);

        incident.ChangeStatus(to, "A note", Reviewer, Now.AddHours(2));

        Assert.Equal(to, incident.Status);
    }

    [Theory]
    [InlineData(IncidentStatus.Reported, IncidentStatus.Logged)]
    [InlineData(IncidentStatus.Reported, IncidentStatus.Reported)]
    [InlineData(IncidentStatus.Logged, IncidentStatus.Logged)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Logged)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Reported)]
    [InlineData(IncidentStatus.Closed, IncidentStatus.Closed)]
    public void ChangeStatus_RefusesEveryOtherMove_AndLeavesTheIncidentAsItWas(IncidentStatus from, IncidentStatus to)
    {
        var incident = IncidentIn(from);
        var changesBefore = incident.StatusChanges.Count;

        var error = Assert.Throws<IncidentStatusChangeNotAllowedError>(() =>
            incident.ChangeStatus(to, "A note", Reviewer, Now.AddHours(2)));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal("incident_status_change_not_allowed", error.ErrorCode);
        Assert.Equal(from, incident.Status);
        Assert.Equal(changesBefore, incident.StatusChanges.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ChangeStatus_RefusesABlankNote(string? note)
    {
        var incident = LogDetectedAt(Now.AddHours(-1));

        var error = Assert.Throws<InvalidIncidentError>(() =>
            incident.ChangeStatus(IncidentStatus.Reported, note!, Reviewer, Now));

        Assert.Equal(400, error.HttpStatusCode);
        Assert.Equal(IncidentStatus.Logged, incident.Status);
        Assert.Empty(incident.StatusChanges);
    }

    [Fact]
    public void ChangeStatus_RefusesANoteOverTheLimit()
    {
        var incident = LogDetectedAt(Now.AddHours(-1));

        Assert.Throws<InvalidIncidentError>(() =>
            incident.ChangeStatus(IncidentStatus.Reported, new string('n', Incident.MaxNoteLength + 1), Reviewer, Now));
        Assert.Empty(incident.StatusChanges);
    }

    [Fact]
    public void ChangeStatus_RefusesAnUnknownStatus()
    {
        var incident = LogDetectedAt(Now.AddHours(-1));

        Assert.Throws<InvalidIncidentError>(() =>
            incident.ChangeStatus((IncidentStatus)42, "A note", Reviewer, Now));
        Assert.Equal(IncidentStatus.Logged, incident.Status);
    }

    [Fact]
    public void ChangeStatus_KeepsEveryChangeInOrder()
    {
        var incident = LogDetectedAt(Now.AddHours(-1));
        incident.ChangeStatus(IncidentStatus.Reported, "Reported to CERT-In", Reviewer, Now.AddHours(1));
        incident.ChangeStatus(IncidentStatus.Closed, "Contained and closed", Reviewer, Now.AddHours(2));

        Assert.Collection(
            incident.StatusChanges,
            first =>
            {
                Assert.Equal(IncidentStatus.Logged, first.FromStatus);
                Assert.Equal(IncidentStatus.Reported, first.ToStatus);
            },
            second =>
            {
                Assert.Equal(IncidentStatus.Reported, second.FromStatus);
                Assert.Equal(IncidentStatus.Closed, second.ToStatus);
                Assert.Equal("Contained and closed", second.Note);
            });
    }
}
