using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>Draw rules: what an author may add, how the exam's scope narrows a rule, and the publish-time check that the bank can fill them.</summary>
public class DrawRuleTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private static Exam NewExam() => new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid(), Now);

    private static Exam Scheduled(Exam exam)
    {
        exam.Schedule(Now.AddDays(1), Now.AddDays(1).AddHours(3), null, null, 3600, Now);
        return exam;
    }

    [Fact]
    public void AddDrawRule_KeepsTheRule_AndNumbersRulesInOrder()
    {
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);

        var first = exam.AddDrawRule(section.Id, 5, null, null, "Easy", " Algebra ", Now);
        var second = exam.AddDrawRule(section.Id, 2, null, null, null, null, Now);

        Assert.Equal(("easy", "algebra"), (first.Difficulty, first.Topic));
        Assert.Equal([1, 2], new[] { first.Order, second.Order });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void AddDrawRule_RefusesACountOutsideOneToOneHundred(int count)
    {
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);

        Assert.Throws<InvalidExamConfigError>(() => exam.AddDrawRule(section.Id, count, null, null, null, null, Now));
    }

    [Fact]
    public void AddDrawRule_RefusesAnUnknownDifficulty()
    {
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);

        Assert.Throws<InvalidExamConfigError>(() => exam.AddDrawRule(section.Id, 1, null, null, "impossible", null, Now));
    }

    [Fact]
    public void AddDrawRule_RefusesAPublishedExam()
    {
        var exam = Scheduled(NewExam());
        var section = exam.AddSection("A", null, Now);
        exam.AddQuestion(section.Id, Guid.NewGuid(), QuestionPlacement.Unfiled, Now);
        exam.Publish(Now);

        Assert.Throws<ExamNotDraftError>(() => exam.AddDrawRule(section.Id, 1, null, null, null, null, Now));
    }

    [Fact]
    public void RemoveDrawRule_TakesItOut_AndRefusesAnUnknownOne()
    {
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);
        var rule = exam.AddDrawRule(section.Id, 1, null, null, null, null, Now);

        exam.RemoveDrawRule(section.Id, rule.Id, Now);

        Assert.Empty(section.DrawRules);
        Assert.Throws<DrawRuleNotFoundError>(() => exam.RemoveDrawRule(section.Id, rule.Id, Now));
    }

    [Fact]
    public void Publish_AcceptsAnExamWhoseOnlyQuestionsComeFromARule()
    {
        var exam = Scheduled(NewExam());
        var section = exam.AddSection("A", null, Now);
        exam.AddDrawRule(section.Id, 3, null, null, null, null, Now);

        exam.Publish(Now);

        Assert.Equal(ExamStatus.Published, exam.Status);
    }

    [Fact]
    public void Publish_StillRefusesAnExamWithNeitherQuestionsNorRules()
    {
        var exam = Scheduled(NewExam());
        exam.AddSection("A", null, Now);

        Assert.Throws<InvalidExamConfigError>(() => exam.Publish(Now));
    }

    [Fact]
    public void Scoping_BookScope_DrawsFromThatBook_AndRefusesAnotherBook()
    {
        var book = Guid.NewGuid();
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);
        var inBook = exam.AddDrawRule(section.Id, 1, null, null, null, null, Now);
        var elsewhere = exam.AddDrawRule(section.Id, 1, Guid.NewGuid(), null, null, null, Now);

        var scoped = DrawRuleScoping.Apply(inBook, ExamScope.ForBook(book));

        Assert.Equal(book, scoped!.BookId);
        Assert.Null(DrawRuleScoping.Apply(elsewhere, ExamScope.ForBook(book)));
    }

    [Fact]
    public void Scoping_ChaptersScope_DrawsFromAllChosenChapters_OrTheRulesOwnIfItIsAmongThem()
    {
        var book = Guid.NewGuid();
        var chapters = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var scope = ExamScope.ForChapters(book, chapters);
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);
        var any = exam.AddDrawRule(section.Id, 1, null, null, null, null, Now);
        var one = exam.AddDrawRule(section.Id, 1, null, chapters[1], null, null, Now);
        var outside = exam.AddDrawRule(section.Id, 1, null, Guid.NewGuid(), null, null, Now);

        Assert.Equal(chapters, DrawRuleScoping.Apply(any, scope)!.ChapterIds);
        Assert.Equal(chapters[1], DrawRuleScoping.Apply(one, scope)!.ChapterId);
        Assert.Null(DrawRuleScoping.Apply(outside, scope));
    }

    [Fact]
    public async Task PoolChecker_RefusesARuleTheBankCannotFill_CountingOnlyQuestionsNotAlreadyInTheExam()
    {
        var bank = Substitute.For<IQuestionBank>();
        var held = Guid.NewGuid();
        bank.FindAsync(Arg.Any<QuestionCriteria>(), Arg.Any<CancellationToken>())
            .Returns([new FoundQuestion(held, null, null), new FoundQuestion(Guid.NewGuid(), null, null)]);
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);
        exam.AddQuestion(section.Id, held, QuestionPlacement.Unfiled, Now);
        exam.AddDrawRule(section.Id, 2, null, null, null, null, Now);

        var error = await Assert.ThrowsAsync<DrawPoolTooSmallError>(() => new DrawPoolChecker(bank).EnsureFillableAsync(exam, CancellationToken.None));

        Assert.Contains("only 1", error.Message);
    }

    [Fact]
    public async Task PoolChecker_AcceptsARuleTheBankCanFill()
    {
        var bank = Substitute.For<IQuestionBank>();
        bank.FindAsync(Arg.Any<QuestionCriteria>(), Arg.Any<CancellationToken>())
            .Returns([new FoundQuestion(Guid.NewGuid(), null, null), new FoundQuestion(Guid.NewGuid(), null, null)]);
        var exam = NewExam();
        var section = exam.AddSection("A", null, Now);
        exam.AddDrawRule(section.Id, 2, null, null, null, null, Now);

        await new DrawPoolChecker(bank).EnsureFillableAsync(exam, CancellationToken.None);
    }
}
