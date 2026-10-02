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
        exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
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

        var first = exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
        var second = exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled);

        Assert.Equal([1, 2], new[] { first.Order, second.Order });
        Assert.Equal(section.Id, first.SectionId);
    }

    [Fact]
    public void AddQuestion_ToAnUnknownSection_ThrowsSectionNotFound()
    {
        var error = Assert.Throws<SectionNotFoundError>(() => NewExam().AddQuestion(Guid.NewGuid(), Guid.NewGuid(), QuestionPlacement.Unfiled));
        Assert.Equal(404, error.HttpStatusCode);
    }

    [Fact]
    public void AddQuestion_TheSameQuestionTwiceInOneSection_ThrowsDuplicate()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        var questionId = Guid.NewGuid();
        exam.AddQuestion(section.Id, questionId, QuestionPlacement.Unfiled);

        var error = Assert.Throws<DuplicateQuestionError>(() => exam.AddQuestion(section.Id, questionId, QuestionPlacement.Unfiled));
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
        exam.AddQuestion(first.Id, questionId, QuestionPlacement.Unfiled);

        Assert.Throws<DuplicateQuestionError>(() => exam.AddQuestion(second.Id, questionId, QuestionPlacement.Unfiled));
    }

    // ---- taking a question out ----------------------------------------------------------------------

    [Fact]
    public void RemoveQuestion_TakesItOut_AndClosesTheGapInTheNumbering()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        var first = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var last = Guid.NewGuid();
        exam.AddQuestion(section.Id, first, QuestionPlacement.Unfiled);
        exam.AddQuestion(section.Id, middle, QuestionPlacement.Unfiled);
        exam.AddQuestion(section.Id, last, QuestionPlacement.Unfiled);

        exam.RemoveQuestion(section.Id, middle);

        Assert.Equal([(first, 1), (last, 2)], section.Questions.Select(q => (q.QuestionVersionId, q.Order)));
    }

    [Fact]
    public void RemoveQuestion_LetsTheSameQuestionBeAddedAgain()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        var questionId = Guid.NewGuid();
        exam.AddQuestion(section.Id, questionId, QuestionPlacement.Unfiled);

        exam.RemoveQuestion(section.Id, questionId);
        var again = exam.AddQuestion(section.Id, questionId, QuestionPlacement.Unfiled);

        Assert.Equal(1, again.Order);
    }

    [Fact]
    public void RemoveQuestion_FromAnUnknownSection_ThrowsSectionNotFound()
    {
        var error = Assert.Throws<SectionNotFoundError>(() => NewExam().RemoveQuestion(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(404, error.HttpStatusCode);
    }

    [Fact]
    public void RemoveQuestion_ThatTheSectionDoesNotHold_ThrowsQuestionNotInExam_AndChangesNothing()
    {
        var exam = NewExam();
        var one = exam.AddSection("One", null);
        var two = exam.AddSection("Two", null);
        var questionId = Guid.NewGuid();
        exam.AddQuestion(one.Id, questionId, QuestionPlacement.Unfiled);

        // It is in the exam, but not in that section: say so rather than quietly removing it from the other one.
        var error = Assert.Throws<QuestionNotInExamError>(() => exam.RemoveQuestion(two.Id, questionId));

        Assert.Equal(404, error.HttpStatusCode);
        Assert.Equal("question_not_in_exam", error.ErrorCode);
        Assert.Single(one.Questions);
    }

    [Fact]
    public void RemoveQuestion_FromAPublishedExam_ThrowsNotDraft()
    {
        // A published exam may already have been sat, and its questions are what its scores mean.
        var exam = ExamReadyToPublish();
        var section = exam.Sections[0];
        var questionId = section.Questions[0].QuestionVersionId;
        exam.Publish(Now);

        var error = Assert.Throws<ExamNotDraftError>(() => exam.RemoveQuestion(section.Id, questionId));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Single(section.Questions);
    }

    // ---- taking a section out -----------------------------------------------------------------------

    [Fact]
    public void RemoveSection_TakesItAndItsQuestionsOut_AndClosesTheGapInTheNumbering()
    {
        var exam = NewExam();
        var one = exam.AddSection("One", null);
        var two = exam.AddSection("Two", null);
        var three = exam.AddSection("Three", null);
        exam.AddQuestion(two.Id, Guid.NewGuid(), QuestionPlacement.Unfiled);

        exam.RemoveSection(two.Id);

        Assert.Equal([(one.Id, 1), (three.Id, 2)], exam.Sections.Select(s => (s.Id, s.Order)));
        Assert.Empty(exam.Sections.SelectMany(s => s.Questions));
    }

    [Fact]
    public void RemoveSection_ThenAddSection_GivesTheNewOneTheNextNumber()
    {
        var exam = NewExam();
        exam.AddSection("One", null);
        var two = exam.AddSection("Two", null);
        exam.AddSection("Three", null);
        exam.RemoveSection(two.Id);

        var added = exam.AddSection("Four", null);

        Assert.Equal([1, 2, 3], exam.Sections.Select(s => s.Order));
        Assert.Equal(3, added.Order);
    }

    [Fact]
    public void RemoveSection_FreesItsQuestionsToBeAddedElsewhereInTheExam()
    {
        var exam = NewExam();
        var one = exam.AddSection("One", null);
        var two = exam.AddSection("Two", null);
        var questionId = Guid.NewGuid();
        exam.AddQuestion(one.Id, questionId, QuestionPlacement.Unfiled);

        exam.RemoveSection(one.Id);

        Assert.Equal(1, exam.AddQuestion(two.Id, questionId, QuestionPlacement.Unfiled).Order);
    }

    [Fact]
    public void RemoveSection_ThatDoesNotExist_ThrowsSectionNotFound_AndChangesNothing()
    {
        var exam = NewExam();
        exam.AddSection("One", null);

        var error = Assert.Throws<SectionNotFoundError>(() => exam.RemoveSection(Guid.NewGuid()));

        Assert.Equal(404, error.HttpStatusCode);
        Assert.Single(exam.Sections);
    }

    [Fact]
    public void RemoveSection_FromAPublishedExam_ThrowsNotDraft()
    {
        var exam = ExamReadyToPublish();
        var section = exam.Sections[0];
        exam.Publish(Now);

        Assert.Throws<ExamNotDraftError>(() => exam.RemoveSection(section.Id));
        Assert.Single(exam.Sections);
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
        exam.AddQuestion(exam.AddSection("S", null).Id, Guid.NewGuid(), QuestionPlacement.Unfiled);

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
        Assert.Throws<ExamNotDraftError>(() => exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled));
        Assert.Throws<ExamNotDraftError>(() => exam.Schedule(Start, End, null, null, null, Now));
    }

    // ---- scope (FR-11)

    private static readonly Guid BookA = Guid.NewGuid();
    private static readonly Guid Algebra = Guid.NewGuid();
    private static readonly Guid Geometry = Guid.NewGuid();
    private static readonly Guid BookB = Guid.NewGuid();
    private static readonly Guid Optics = Guid.NewGuid();

    [Fact]
    public void ANewExam_IsIndependent_AndTakesAnyQuestion()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);

        Assert.Equal(ExamScopeType.Independent, exam.Scope.Type);
        exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
        exam.AddQuestion(section.Id, Guid.NewGuid(), new QuestionPlacement(BookA, Algebra));
        Assert.Equal(2, section.Questions.Count);
    }

    [Fact]
    public void AChapterExam_TakesQuestionsFromItsChapter_AndRefusesAnyOther()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        exam.SetScope(ExamScope.ForChapters(BookA, [Algebra]), new Dictionary<Guid, QuestionPlacement>());

        exam.AddQuestion(section.Id, Guid.NewGuid(), new QuestionPlacement(BookA, Algebra));

        var sibling = Guid.NewGuid();
        var error = Assert.Throws<QuestionOutsideExamScopeError>(() => exam.AddQuestion(section.Id, sibling, new QuestionPlacement(BookA, Geometry)));
        Assert.Equal("question_outside_scope", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal([sibling], error.QuestionIds);
        Assert.Throws<QuestionOutsideExamScopeError>(() => exam.AddQuestion(section.Id, Guid.NewGuid(), new QuestionPlacement(BookB, Optics)));
        Assert.Throws<QuestionOutsideExamScopeError>(() => exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled));
        Assert.Single(section.Questions);
    }

    [Fact]
    public void AWholeBookExam_TakesAnyChapterOfTheBook_ButNotAnotherBook()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        exam.SetScope(ExamScope.ForBook(BookA), new Dictionary<Guid, QuestionPlacement>());

        exam.AddQuestion(section.Id, Guid.NewGuid(), new QuestionPlacement(BookA, Algebra));
        exam.AddQuestion(section.Id, Guid.NewGuid(), new QuestionPlacement(BookA, Geometry));

        Assert.Throws<QuestionOutsideExamScopeError>(() => exam.AddQuestion(section.Id, Guid.NewGuid(), new QuestionPlacement(BookB, Optics)));
        Assert.Equal(2, section.Questions.Count);
    }

    [Fact]
    public void ChangingTheScope_IsRefused_WhenTheExamAlreadyHoldsAQuestionOutsideIt_AndNothingChanges()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        var inAlgebra = Guid.NewGuid();
        var inGeometry = Guid.NewGuid();
        exam.AddQuestion(section.Id, inAlgebra, new QuestionPlacement(BookA, Algebra));
        exam.AddQuestion(section.Id, inGeometry, new QuestionPlacement(BookA, Geometry));
        var placements = new Dictionary<Guid, QuestionPlacement>
        {
            [inAlgebra] = new(BookA, Algebra),
            [inGeometry] = new(BookA, Geometry),
        };

        var error = Assert.Throws<QuestionOutsideExamScopeError>(() => exam.SetScope(ExamScope.ForChapters(BookA, [Algebra]), placements));

        Assert.Equal([inGeometry], error.QuestionIds);
        Assert.Equal(ExamScopeType.Independent, exam.Scope.Type);
        // A scope that holds them all is fine, and so is lifting the limit again.
        exam.SetScope(ExamScope.ForBook(BookA), placements);
        Assert.Equal(ExamScopeType.Book, exam.Scope.Type);
        exam.SetScope(ExamScope.Independent(), placements);
        Assert.Equal(ExamScopeType.Independent, exam.Scope.Type);
    }

    [Fact]
    public void AQuestionTheBankDidNotReportAPlacementFor_CountsAsNotFiled_SoAScopeRefusesIt()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        var orphan = Guid.NewGuid();
        exam.AddQuestion(section.Id, orphan, QuestionPlacement.Unfiled);

        var error = Assert.Throws<QuestionOutsideExamScopeError>(() => exam.SetScope(ExamScope.ForBook(BookA), new Dictionary<Guid, QuestionPlacement>()));

        Assert.Equal([orphan], error.QuestionIds);
    }

    [Fact]
    public void ThePlacementIsCheckedBeforeTheDuplicateRule_SoARefusedQuestionIsNeverReportedAsAClash()
    {
        var exam = NewExam();
        var section = exam.AddSection("S", null);
        var id = Guid.NewGuid();
        exam.AddQuestion(section.Id, id, new QuestionPlacement(BookA, Algebra));
        exam.SetScope(ExamScope.ForChapters(BookA, [Algebra]), new Dictionary<Guid, QuestionPlacement> { [id] = new(BookA, Algebra) });

        Assert.Throws<DuplicateQuestionError>(() => exam.AddQuestion(section.Id, id, new QuestionPlacement(BookA, Algebra)));
        Assert.Throws<QuestionOutsideExamScopeError>(() => exam.AddQuestion(section.Id, id, new QuestionPlacement(BookA, Geometry)));
    }

    [Fact]
    public void APublishedExam_RefusesAScopeChange()
    {
        var exam = NewExam();
        exam.AddQuestion(exam.AddSection("S", null).Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.Publish(Now);

        Assert.Throws<ExamNotDraftError>(() => exam.SetScope(ExamScope.ForBook(BookA), new Dictionary<Guid, QuestionPlacement>()));
    }
}
