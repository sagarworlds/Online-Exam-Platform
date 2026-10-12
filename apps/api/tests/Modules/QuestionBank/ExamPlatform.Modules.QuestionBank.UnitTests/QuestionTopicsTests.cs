using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>The topic labels other modules read a question through (FR-36): labels only, and only for the questions that have them.</summary>
public class QuestionTopicsTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IQuestionRepository _questions = Substitute.For<IQuestionRepository>();

    private Question Labelled(params string?[] topics) =>
        Question.Create("<p>Capital of France?</p>", [new("Paris", true), new("Rome", false)], Author, Now, topics: topics);

    private QuestionTopics Topics(params Question[] bankHolds)
    {
        _questions.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Question>>(
                bankHolds.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        return new QuestionTopics(_questions);
    }

    [Fact]
    public async Task ALabelledQuestion_IsReturnedWithItsLabels()
    {
        var question = Labelled("algebra", "graphs");

        var topics = await Topics(question).GetTopicsAsync([question.Id], CancellationToken.None);

        Assert.Equal(["algebra", "graphs"], topics[question.Id]);
    }

    [Fact]
    public async Task AQuestionWithNoLabel_IsAbsent_NotFiledUnderAnEmptyName()
    {
        var question = Labelled();

        var topics = await Topics(question).GetTopicsAsync([question.Id], CancellationToken.None);

        Assert.Empty(topics);
    }

    [Fact]
    public async Task NoIds_ReadsNothing()
    {
        var topics = await Topics().GetTopicsAsync([], CancellationToken.None);

        Assert.Empty(topics);
        await _questions.DidNotReceiveWithAnyArgs().GetManyAsync(default!, default);
    }
}
