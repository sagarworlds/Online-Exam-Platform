using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>A problem a candidate reports from inside an exam (FR-42): raised with a kind and a description, resolved once by staff.</summary>
public class IssueReportTests
{
    private readonly Guid _attempt = Guid.NewGuid();
    private readonly Guid _exam = Guid.NewGuid();
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _question = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();

    private IssueReport Open(string? message = "Option C is missing", Guid? question = null, IssueCategory category = IssueCategory.Question) =>
        IssueReport.Raise(_attempt, _exam, _candidate, question, category, message, Fixtures.Now);

    [Fact]
    public void ARaisedReport_IsOpen_KeepsWhoWhatAndWhen_AndRaisesAnEventWithoutTheMessage()
    {
        var report = Open("  Option C is missing  ", _question);

        Assert.Equal(IssueReportStatus.Open, report.Status);
        Assert.Equal(_attempt, report.AttemptId);
        Assert.Equal(_exam, report.ExamId);
        Assert.Equal(_candidate, report.CandidateId);
        Assert.Equal(_question, report.QuestionId);
        Assert.Equal(IssueCategory.Question, report.Category);
        Assert.Equal("Option C is missing", report.Message);
        Assert.Equal(Fixtures.Now, report.ReportedAtUtc);
        Assert.Null(report.ResolvedAtUtc);
        Assert.Null(report.ResolvedByUserId);
        Assert.Null(report.ResolutionNote);

        var raised = Assert.IsType<IssueReportedEvent>(Assert.Single(report.DomainEvents));
        Assert.Equal(report.Id, raised.IssueReportId);
        Assert.Equal(IssueCategory.Question, raised.Category);
        // The event is what the audit trail is written from; the candidate's words are not in it.
        Assert.DoesNotContain(nameof(IssueReport.Message), typeof(IssueReportedEvent).GetProperties().Select(p => p.Name));
    }

    [Theory]
    [InlineData(IssueCategory.Question)]
    [InlineData(IssueCategory.Technical)]
    [InlineData(IssueCategory.Other)]
    public void AReport_MayBeOfAnyKind_AndNeedNotNameAQuestion(IssueCategory category)
    {
        var report = Open(category: category);

        Assert.Equal(category, report.Category);
        Assert.Null(report.QuestionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AReportWithoutADescription_IsRefused(string? message)
    {
        var error = Assert.Throws<InvalidAttemptError>(() => Open(message));

        Assert.Contains("what is wrong", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADescription_MayBeAsLongAsTheLimit_ButNotLonger()
    {
        Assert.Equal(IssueReport.MaxMessageLength, Open(new string('x', IssueReport.MaxMessageLength)).Message.Length);

        Assert.Throws<InvalidAttemptError>(() => Open(new string('x', IssueReport.MaxMessageLength + 1)));
    }

    [Fact]
    public void ALimitIsCountedAfterTheWhitespaceAroundTheDescriptionIsRemoved()
    {
        var padded = "  " + new string('x', IssueReport.MaxMessageLength) + "  ";

        Assert.Equal(IssueReport.MaxMessageLength, Open(padded).Message.Length);
    }

    [Fact]
    public void AKindThatIsNotOneOfOurs_IsRefused()
    {
        Assert.Throws<InvalidAttemptError>(() => Open(category: (IssueCategory)99));
    }

    [Fact]
    public void Resolving_RecordsWhoWhenAndWhy_AndRaisesAnEvent()
    {
        var report = Open();
        report.ClearDomainEvents();
        var resolvedAt = Fixtures.Now.AddMinutes(3);

        report.Resolve(_staff, resolvedAt, "  Fixed the option  ");

        Assert.Equal(IssueReportStatus.Resolved, report.Status);
        Assert.Equal(_staff, report.ResolvedByUserId);
        Assert.Equal(resolvedAt, report.ResolvedAtUtc);
        Assert.Equal("Fixed the option", report.ResolutionNote);
        var resolved = Assert.IsType<IssueResolvedEvent>(Assert.Single(report.DomainEvents));
        Assert.Equal(report.Id, resolved.IssueReportId);
        Assert.Equal(_candidate, resolved.CandidateId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANoteIsOptional_AndABlankOneIsNoNote(string? note)
    {
        var report = Open();

        report.Resolve(_staff, Fixtures.Now, note);

        Assert.Equal(IssueReportStatus.Resolved, report.Status);
        Assert.Null(report.ResolutionNote);
    }

    [Fact]
    public void ANote_IsLimited()
    {
        var report = Open();

        Assert.Throws<InvalidAttemptError>(() => report.Resolve(_staff, Fixtures.Now, new string('x', IssueReport.MaxNoteLength + 1)));

        Assert.Equal(IssueReportStatus.Open, report.Status);
    }

    [Fact]
    public void AResolvedReport_CannotBeResolvedAgain_AndKeepsWhatWasRecorded()
    {
        var report = Open();
        report.Resolve(_staff, Fixtures.Now, "First answer");

        var error = Assert.Throws<IssueReportNotOpenError>(() => report.Resolve(Guid.NewGuid(), Fixtures.Now.AddHours(1), "Second answer"));

        Assert.Equal("issue_report_not_open", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal(_staff, report.ResolvedByUserId);
        Assert.Equal("First answer", report.ResolutionNote);
    }
}
