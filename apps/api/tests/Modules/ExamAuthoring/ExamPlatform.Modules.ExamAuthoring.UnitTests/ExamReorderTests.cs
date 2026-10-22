using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>Putting the sections of a draft exam, and the questions of a section, in a new order (FR-11).</summary>
public class ExamReorderTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(1);

    private static Exam NewExam() => new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid(), Now);

    private static (Exam Exam, ExamSection Algebra, ExamSection Geometry, ExamSection Statistics) ExamWithThreeSections()
    {
        var exam = NewExam();
        var algebra = exam.AddSection("Algebra", null, Now);
        var geometry = exam.AddSection("Geometry", null, Now);
        var statistics = exam.AddSection("Statistics", null, Now);
        return (exam, algebra, geometry, statistics);
    }

    private static (Exam Exam, ExamSection Section, Guid[] Questions) ExamWithThreeQuestions()
    {
        var exam = NewExam();
        var section = exam.AddSection("Algebra", null, Now);
        var questions = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var question in questions)
            exam.AddQuestion(section.Id, question, QuestionPlacement.Unfiled, Now);

        return (exam, section, questions);
    }

    private static Exam Published(Exam exam)
    {
        exam.Schedule(Now.AddDays(1), Now.AddDays(1).AddHours(3), null, null, 3600, Now);
        exam.Publish(Now);
        return exam;
    }

    // ---- sections ------------------------------------------------------------------------------------

    [Fact]
    public void ReorderSections_NumbersThemInTheOrderGiven()
    {
        var (exam, algebra, geometry, statistics) = ExamWithThreeSections();

        exam.ReorderSections([statistics.Id, algebra.Id, geometry.Id], Later);

        Assert.Equal([(statistics.Id, 1), (algebra.Id, 2), (geometry.Id, 3)], exam.Sections.OrderBy(s => s.Order).Select(s => (s.Id, s.Order)));
        Assert.Equal(Later, exam.UpdatedAt);
    }

    [Fact]
    public void ReorderSections_InTheOrderTheyAlreadyAreIn_ChangesNothingButIsAllowed()
    {
        var (exam, algebra, geometry, statistics) = ExamWithThreeSections();

        exam.ReorderSections([algebra.Id, geometry.Id, statistics.Id], Later);

        Assert.Equal([1, 2, 3], new[] { algebra.Order, geometry.Order, statistics.Order });
    }

    [Fact]
    public void ReorderSections_KeepsTheNextSectionAddedAfterTheLastOne()
    {
        var (exam, algebra, geometry, statistics) = ExamWithThreeSections();
        exam.ReorderSections([geometry.Id, statistics.Id, algebra.Id], Later);

        var added = exam.AddSection("Probability", null, Later);

        Assert.Equal(4, added.Order);
    }

    [Fact]
    public void ReorderSections_KeepsEachSectionsQuestionsWithIt()
    {
        var (exam, algebra, geometry, _) = ExamWithThreeSections();
        var question = Guid.NewGuid();
        exam.AddQuestion(algebra.Id, question, QuestionPlacement.Unfiled, Now);

        exam.ReorderSections([geometry.Id, algebra.Id, exam.Sections[2].Id], Later);

        Assert.Equal(question, Assert.Single(algebra.Questions).QuestionVersionId);
        Assert.Equal(1, algebra.Questions[0].Order);
    }

    [Fact]
    public void ReorderSections_WithASectionLeftOut_ThrowsInvalidConfig_AndChangesNothing()
    {
        var (exam, algebra, geometry, statistics) = ExamWithThreeSections();

        var error = Assert.Throws<InvalidExamConfigError>(() => exam.ReorderSections([statistics.Id, algebra.Id], Later));

        Assert.Equal(400, error.HttpStatusCode);
        Assert.Equal([1, 2, 3], new[] { algebra.Order, geometry.Order, statistics.Order });
        Assert.Equal(Now, exam.UpdatedAt);
    }

    [Fact]
    public void ReorderSections_WithASectionRepeated_ThrowsInvalidConfig()
    {
        var (exam, algebra, geometry, _) = ExamWithThreeSections();

        Assert.Throws<InvalidExamConfigError>(() => exam.ReorderSections([algebra.Id, geometry.Id, geometry.Id], Later));
        Assert.Equal(1, algebra.Order);
    }

    [Fact]
    public void ReorderSections_NamingASectionTheExamDoesNotHave_ThrowsInvalidConfig()
    {
        var (exam, algebra, geometry, _) = ExamWithThreeSections();

        Assert.Throws<InvalidExamConfigError>(() => exam.ReorderSections([algebra.Id, geometry.Id, Guid.NewGuid()], Later));
    }

    [Fact]
    public void ReorderSections_WithNoList_ThrowsInvalidConfig_ratherThanCrashing()
    {
        var (exam, _, _, _) = ExamWithThreeSections();

        Assert.Throws<InvalidExamConfigError>(() => exam.ReorderSections(null, Later));
    }

    [Fact]
    public void ReorderSections_OfAnExamWithNoSections_AcceptsAnEmptyList()
    {
        var exam = NewExam();

        exam.ReorderSections([], Later);

        Assert.Empty(exam.Sections);
    }

    [Fact]
    public void ReorderSections_OfAPublishedExam_ThrowsNotDraft_AndChangesNothing()
    {
        var (exam, algebra, geometry, statistics) = ExamWithThreeSections();
        exam.AddQuestion(algebra.Id, Guid.NewGuid(), QuestionPlacement.Unfiled, Now);
        Published(exam);

        var error = Assert.Throws<ExamNotDraftError>(() => exam.ReorderSections([statistics.Id, geometry.Id, algebra.Id], Later));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal(1, algebra.Order);
    }

    // ---- questions -----------------------------------------------------------------------------------

    [Fact]
    public void ReorderQuestions_NumbersThemInTheOrderGiven()
    {
        var (exam, section, questions) = ExamWithThreeQuestions();

        exam.ReorderQuestions(section.Id, [questions[2], questions[0], questions[1]], Later);

        Assert.Equal([(questions[2], 1), (questions[0], 2), (questions[1], 3)], section.Questions.OrderBy(q => q.Order).Select(q => (q.QuestionVersionId, q.Order)));
        Assert.Equal(Later, exam.UpdatedAt);
    }

    [Fact]
    public void ReorderQuestions_KeepsTheNextQuestionAddedAfterTheLastOne()
    {
        var (exam, section, questions) = ExamWithThreeQuestions();
        exam.ReorderQuestions(section.Id, [questions[1], questions[2], questions[0]], Later);

        var added = exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled, Later);

        Assert.Equal(4, added.Order);
    }

    [Fact]
    public void ReorderQuestions_OnlyTouchesTheSectionNamed()
    {
        var (exam, section, questions) = ExamWithThreeQuestions();
        var other = exam.AddSection("Geometry", null, Now);
        var inOther = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var question in inOther)
            exam.AddQuestion(other.Id, question, QuestionPlacement.Unfiled, Now);

        exam.ReorderQuestions(section.Id, [questions[2], questions[1], questions[0]], Later);

        Assert.Equal(inOther, other.Questions.OrderBy(q => q.Order).Select(q => q.QuestionVersionId));
    }

    [Fact]
    public void ReorderQuestions_OfAnUnknownSection_ThrowsSectionNotFound()
    {
        var error = Assert.Throws<SectionNotFoundError>(() => NewExam().ReorderQuestions(Guid.NewGuid(), [], Later));

        Assert.Equal(404, error.HttpStatusCode);
    }

    [Fact]
    public void ReorderQuestions_WithAQuestionLeftOut_ThrowsInvalidConfig_AndChangesNothing()
    {
        var (exam, section, questions) = ExamWithThreeQuestions();

        Assert.Throws<InvalidExamConfigError>(() => exam.ReorderQuestions(section.Id, [questions[2], questions[0]], Later));

        Assert.Equal(questions, section.Questions.OrderBy(q => q.Order).Select(q => q.QuestionVersionId));
        Assert.Equal(Now, exam.UpdatedAt);
    }

    [Fact]
    public void ReorderQuestions_WithAQuestionRepeated_ThrowsInvalidConfig()
    {
        var (exam, section, questions) = ExamWithThreeQuestions();

        Assert.Throws<InvalidExamConfigError>(() => exam.ReorderQuestions(section.Id, [questions[0], questions[0], questions[1]], Later));
    }

    [Fact]
    public void ReorderQuestions_NamingAQuestionTheSectionDoesNotHold_ThrowsInvalidConfig_evenIfAnotherSectionDoes()
    {
        var (exam, section, questions) = ExamWithThreeQuestions();
        var other = exam.AddSection("Geometry", null, Now);
        var inOther = Guid.NewGuid();
        exam.AddQuestion(other.Id, inOther, QuestionPlacement.Unfiled, Now);

        Assert.Throws<InvalidExamConfigError>(() => exam.ReorderQuestions(section.Id, [questions[0], questions[1], inOther], Later));
    }

    [Fact]
    public void ReorderQuestions_WithNoList_ThrowsInvalidConfig_ratherThanCrashing()
    {
        var (exam, section, _) = ExamWithThreeQuestions();

        Assert.Throws<InvalidExamConfigError>(() => exam.ReorderQuestions(section.Id, null, Later));
    }

    [Fact]
    public void ReorderQuestions_OfAPublishedExam_ThrowsNotDraft_AndChangesNothing()
    {
        // A published exam may already have been sat, and its questions are what its scores mean.
        var (exam, section, questions) = ExamWithThreeQuestions();
        Published(exam);

        var error = Assert.Throws<ExamNotDraftError>(() => exam.ReorderQuestions(section.Id, [questions[2], questions[1], questions[0]], Later));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal(questions, section.Questions.OrderBy(q => q.Order).Select(q => q.QuestionVersionId));
    }

    // ---- the handlers --------------------------------------------------------------------------------

    public class Handlers
    {
        private readonly IExamRepository repository = Substitute.For<IExamRepository>();
        private readonly IExamAuthoringUnitOfWork unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
        private readonly Exam exam = NewExam();

        public Handlers() => repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);

        [Fact]
        public async Task ReorderSections_PutsThemInOrder_AndSaves()
        {
            var first = exam.AddSection("One", null, Now);
            var second = exam.AddSection("Two", null, Now);

            await new ReorderSectionsHandler(repository, unitOfWork, TestClock.At(Later))
                .HandleAsync(new ReorderSectionsCommand(exam.Id, [second.Id, first.Id]), CancellationToken.None);

            Assert.Equal([second.Id, first.Id], exam.Sections.OrderBy(s => s.Order).Select(s => s.Id));
            await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ReorderSections_ForAnUnknownExam_ThrowsAndSavesNothing()
        {
            var unknown = Guid.NewGuid();
            repository.GetByIdOrThrowAsync(unknown, Arg.Any<CancellationToken>()).Returns<Exam>(_ => throw new ExamNotFoundError(unknown));

            await Assert.ThrowsAsync<ExamNotFoundError>(() =>
                new ReorderSectionsHandler(repository, unitOfWork, TestClock.At(Later)).HandleAsync(new ReorderSectionsCommand(unknown, []), CancellationToken.None));

            await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ReorderSections_WithAListThatIsNotTheSections_ThrowsAndSavesNothing()
        {
            exam.AddSection("One", null, Now);

            await Assert.ThrowsAsync<InvalidExamConfigError>(() =>
                new ReorderSectionsHandler(repository, unitOfWork, TestClock.At(Later)).HandleAsync(new ReorderSectionsCommand(exam.Id, null), CancellationToken.None));

            await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ReorderQuestions_PutsThemInOrder_AndSaves()
        {
            var section = exam.AddSection("One", null, Now);
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            exam.AddQuestion(section.Id, a, QuestionPlacement.Unfiled, Now);
            exam.AddQuestion(section.Id, b, QuestionPlacement.Unfiled, Now);

            await new ReorderQuestionsHandler(repository, unitOfWork, TestClock.At(Later))
                .HandleAsync(new ReorderQuestionsCommand(exam.Id, section.Id, [b, a]), CancellationToken.None);

            Assert.Equal([b, a], section.Questions.OrderBy(q => q.Order).Select(q => q.QuestionVersionId));
            await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ReorderQuestions_ForAnUnknownSection_ThrowsAndSavesNothing()
        {
            await Assert.ThrowsAsync<SectionNotFoundError>(() =>
                new ReorderQuestionsHandler(repository, unitOfWork, TestClock.At(Later)).HandleAsync(new ReorderQuestionsCommand(exam.Id, Guid.NewGuid(), []), CancellationToken.None));

            await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }
}
