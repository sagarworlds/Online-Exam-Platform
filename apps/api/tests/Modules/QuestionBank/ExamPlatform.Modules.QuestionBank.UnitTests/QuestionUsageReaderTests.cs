using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Where a question is in use is the only thing the edit and delete rules depend on, so the way sources are combined is pinned here.</summary>
public class QuestionUsageReaderTests
{
    private static readonly Guid Q1 = Guid.NewGuid();
    private static readonly Guid Q2 = Guid.NewGuid();

    private static IQuestionUsageSource SourceReturning(params QuestionUse[] uses)
    {
        var source = Substitute.For<IQuestionUsageSource>();
        source.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(uses);
        return source;
    }

    private static Task<IReadOnlyDictionary<Guid, QuestionUsageDto>> Read(IEnumerable<IQuestionUsageSource> sources, params Guid[] ids) =>
        new QuestionUsageReader(sources).ReadAsync(ids, CancellationToken.None);

    [Fact]
    public async Task AQuestionNothingUses_IsReportedUnused_NotLeftOut()
    {
        var usage = await Read([SourceReturning()], Q1);

        Assert.Equal(QuestionUsageDto.Unused, usage[Q1]);
        Assert.False(usage[Q1].IsInAnyExam);
    }

    [Fact]
    public async Task WithNoSourcesRegistered_EveryQuestionIsUnused()
    {
        var usage = await Read([], Q1, Q2);

        Assert.Equal(2, usage.Count);
        Assert.All(usage.Values, u => Assert.Equal(QuestionUsageDto.Unused, u));
    }

    [Fact]
    public async Task UsesFromEverySource_AreCombinedPerQuestion()
    {
        var exams = SourceReturning(
            new QuestionUse(Q1, QuestionUseKind.InExam, "Maths mock"),
            new QuestionUse(Q1, QuestionUseKind.InExam, "Algebra test"),
            new QuestionUse(Q2, QuestionUseKind.InExam, "Maths mock"));
        var answers = SourceReturning(new QuestionUse(Q1, QuestionUseKind.Answered, "Answered by candidates"));

        var usage = await Read([exams, answers], Q1, Q2);

        Assert.Equal(2, usage[Q1].ExamCount);
        Assert.Equal(["Algebra test", "Maths mock"], usage[Q1].ExamNames);
        Assert.True(usage[Q1].Answered);
        Assert.Equal(1, usage[Q2].ExamCount);
        Assert.False(usage[Q2].Answered);
    }

    [Fact]
    public async Task AnAnsweredQuestion_IsNotCountedAsAnExam()
    {
        var usage = await Read([SourceReturning(new QuestionUse(Q1, QuestionUseKind.Answered, "Answered by candidates"))], Q1);

        Assert.Equal(0, usage[Q1].ExamCount);
        Assert.Empty(usage[Q1].ExamNames);
        Assert.True(usage[Q1].Answered);
    }

    [Fact]
    public async Task ManyExams_AreCountedInFull_ButOnlySomeAreNamed()
    {
        var uses = Enumerable.Range(1, 12).Select(i => new QuestionUse(Q1, QuestionUseKind.InExam, $"Exam {i:00}")).ToArray();

        var usage = await Read([SourceReturning(uses)], Q1);

        Assert.Equal(12, usage[Q1].ExamCount);
        Assert.Equal(QuestionUsageDto.MaxNamedExams, usage[Q1].ExamNames.Count);
        Assert.Equal("Exam 01", usage[Q1].ExamNames[0]);
    }

    [Fact]
    public async Task AskingForNoQuestions_AsksNoSource()
    {
        var source = SourceReturning();

        var usage = await Read([source], []);

        Assert.Empty(usage);
        await source.DidNotReceiveWithAnyArgs().FindAsync(default!, default);
    }

    [Fact]
    public async Task EverySource_IsAskedOnceForTheWholePage_NotOncePerQuestion()
    {
        var source = SourceReturning();

        await Read([source], Q1, Q2, Q1);

        await source.Received(1).FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASourceThatFails_FailsTheRead_SoAQuestionIsNeverTakenForUnusedByMistake()
    {
        var broken = Substitute.For<IQuestionUsageSource>();
        broken.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<QuestionUse>>(_ => throw new InvalidOperationException("database is down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Read([broken], Q1));
    }

    [Fact]
    public async Task ReadOne_ReturnsThatQuestionsUsage()
    {
        var reader = new QuestionUsageReader([SourceReturning(new QuestionUse(Q1, QuestionUseKind.InExam, "Maths mock"))]);

        var usage = await reader.ReadOneAsync(Q1, CancellationToken.None);

        Assert.Equal(1, usage.ExamCount);
    }
}
