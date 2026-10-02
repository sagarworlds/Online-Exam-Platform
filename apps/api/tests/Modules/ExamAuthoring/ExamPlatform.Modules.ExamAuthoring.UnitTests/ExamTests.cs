using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class ExamTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = Now.AddDays(1);
    private static readonly DateTime End = Now.AddDays(1).AddHours(3);

    private static Exam NewExam() =>
        new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    private static Exam ExamReadyToPublish()
    {
        var exam = NewExam();
        var section = exam.AddSection("Algebra", null);
        exam.AddQuestion(section.Id, Guid.NewGuid());
        exam.Schedule(Start, End, null, null, 3600, Now);
        return exam;
    }

    // ---- Schedule (FR-13) -------------------------------------------------------------------------

    [Fact]
    public void NewExam_IsNotScheduled() => Assert.False(NewExam().IsScheduled);

    [Fact]
    public void Schedule_SetsTheWindowDurationAndLateEntry_AndKeepsTheOtherConfig()
    {
        var exam = NewExam();
        var lateEntry = Start.AddMinutes(30);

        exam.Schedule(Start, End, " Asia/Kolkata ", lateEntry, 5400, Now);

        Assert.True(exam.IsScheduled);
        Assert.Equal(Start, exam.ScheduledStartTime);
        Assert.Equal(End, exam.ScheduledEndTime);
        Assert.Equal(lateEntry, exam.LateEntryDeadline);
        Assert.Equal("Asia/Kolkata", exam.TimeZone);
        Assert.Equal(5400, exam.Config.TotalTimeSeconds);
        Assert.True(exam.Config.ShuffleQuestions);
        Assert.Equal(1m, exam.Config.MarkingScheme.CorrectMarks);
    }

    [Fact]
    public void Schedule_WithoutATimeZone_KeepsTheDefault()
    {
        var exam = NewExam();

        exam.Schedule(Start, End, "  ", null, null, Now);

        Assert.Equal("Asia/Kolkata", exam.TimeZone);
        Assert.Null(exam.Config.TotalTimeSeconds);
    }

    [Fact]
    public void Schedule_CanBeRedoneWhileDraft()
    {
        var exam = NewExam();
        exam.Schedule(Start, End, null, null, 3600, Now);

        exam.Schedule(Start.AddDays(1), End.AddDays(1), null, null, null, Now);

        Assert.Equal(Start.AddDays(1), exam.ScheduledStartTime);
        Assert.Null(exam.Config.TotalTimeSeconds);
    }

    [Fact]
    public void Schedule_WithEndNotAfterStart_Throws() =>
        Assert.Throws<InvalidExamConfigError>(() => NewExam().Schedule(End, Start, null, null, null, Now));

    [Fact]
    public void Schedule_WithEndInThePast_Throws() =>
        Assert.Throws<InvalidExamConfigError>(() => NewExam().Schedule(Now.AddDays(-2), Now.AddDays(-1), null, null, null, Now));

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Schedule_WithLateEntryOutsideTheWindow_Throws(int minutesFromStart) =>
        Assert.Throws<InvalidExamConfigError>(() =>
            NewExam().Schedule(Start, End, null, Start.AddMinutes(minutesFromStart), null, Now));

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Schedule_WithANonPositiveDuration_Throws(int seconds) =>
        Assert.Throws<InvalidExamConfigError>(() => NewExam().Schedule(Start, End, null, null, seconds, Now));

    [Fact]
    public void Schedule_WithADurationLongerThanTheWindow_Throws() =>
        Assert.Throws<InvalidExamConfigError>(() => NewExam().Schedule(Start, End, null, null, 3 * 3600 + 1, Now));

    [Fact]
    public void Schedule_WithATooLongTimeZone_Throws() =>
        Assert.Throws<InvalidExamConfigError>(() =>
            NewExam().Schedule(Start, End, new string('x', Exam.MaxTimeZoneLength + 1), null, null, Now));

    // ---- sections and questions -------------------------------------------------------------------

    [Fact]
    public void AddSection_NumbersSectionsInOrderAndTrimsTheName()
    {
        var exam = NewExam();

        var first = exam.AddSection("  Algebra ", 1800);
        var second = exam.AddSection("Geometry", null);

        Assert.Equal("Algebra", first.Name);
        Assert.Equal([1, 2], new[] { first.Order, second.Order });
        Assert.Equal(exam.Id, first.ExamId);
        Assert.Equal(2, exam.Sections.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void AddSection_WithABlankName_Throws(string? name) =>
        Assert.Throws<InvalidExamConfigError>(() => NewExam().AddSection(name!, null));

    [Fact]
    public void AddSection_WithANonPositiveTimeLimit_Throws() =>
        Assert.Throws<InvalidExamConfigError>(() => NewExam().AddSection("S", 0));

    [Fact]
    public void AddQuestion_AppendsInOrderWithinTheSection()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);

        var first = exam.AddQuestion(section.Id, Guid.NewGuid());
        var second = exam.AddQuestion(section.Id, Guid.NewGuid());

        Assert.Equal([1, 2], new[] { first.Order, second.Order });
        Assert.Equal(section.Id, first.SectionId);
    }

    [Fact]
    public void AddQuestion_ToAnUnknownSection_ThrowsSectionNotFound()
    {
        var error = Assert.Throws<SectionNotFoundError>(() => NewExam().AddQuestion(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(404, error.HttpStatusCode);
    }

    [Fact]
    public void AddQuestion_TheSameQuestionTwiceInOneSection_ThrowsDuplicate()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        var questionId = Guid.NewGuid();
        exam.AddQuestion(section.Id, questionId);

        var error = Assert.Throws<DuplicateQuestionError>(() => exam.AddQuestion(section.Id, questionId));
        Assert.Equal(409, error.HttpStatusCode);
    }

    [Fact]
    public void AddQuestion_TheSameQuestionInTwoSections_ThrowsDuplicate()
    {
        // A question that appeared in two sections would be asked and scored twice.
        var exam = NewExam();
        var first = exam.AddSection("One", null);
        var second = exam.AddSection("Two", null);
        var questionId = Guid.NewGuid();
        exam.AddQuestion(first.Id, questionId);

        Assert.Throws<DuplicateQuestionError>(() => exam.AddQuestion(second.Id, questionId));
    }

    // ---- publish ----------------------------------------------------------------------------------

    [Fact]
    public void Publish_AScheduledExamWithAQuestion_PublishesAndRaisesTheEvent()
    {
        var exam = ExamReadyToPublish();

        exam.Publish(Now);

        Assert.Equal(ExamStatus.Published, exam.Status);
        Assert.Contains(exam.DomainEvents, e => e is ExamPlatform.Modules.ExamAuthoring.Domain.Events.ExamPublishedEvent);
    }

    [Fact]
    public void Publish_WithoutASchedule_Throws()
    {
        var exam = NewExam();
        exam.AddQuestion(exam.AddSection("S", null).Id, Guid.NewGuid());

        Assert.Throws<InvalidExamConfigError>(() => exam.Publish(Now));
        Assert.Equal(ExamStatus.Draft, exam.Status);
    }

    [Fact]
    public void Publish_WithoutAnyQuestion_Throws()
    {
        var exam = NewExam();
        exam.AddSection("Empty", null);
        exam.Schedule(Start, End, null, null, null, Now);

        Assert.Throws<InvalidExamConfigError>(() => exam.Publish(Now));
    }

    [Fact]
    public void Publish_AfterTheWindowHasEnded_Throws()
    {
        var exam = ExamReadyToPublish();

        Assert.Throws<InvalidExamConfigError>(() => exam.Publish(End.AddMinutes(1)));
    }

    [Fact]
    public void Publish_Twice_ThrowsNotDraft()
    {
        var exam = ExamReadyToPublish();
        exam.Publish(Now);

        var error = Assert.Throws<ExamNotDraftError>(() => exam.Publish(Now));
        Assert.Equal(409, error.HttpStatusCode);
    }

    [Fact]
    public void APublishedExam_CannotBeEdited()
    {
        var exam = ExamReadyToPublish();
        var section = exam.Sections[0];
        exam.Publish(Now);

        Assert.Throws<ExamNotDraftError>(() => exam.AddSection("Late", null));
        Assert.Throws<ExamNotDraftError>(() => exam.AddQuestion(section.Id, Guid.NewGuid()));
        Assert.Throws<ExamNotDraftError>(() => exam.Schedule(Start, End, null, null, null, Now));
    }
}
