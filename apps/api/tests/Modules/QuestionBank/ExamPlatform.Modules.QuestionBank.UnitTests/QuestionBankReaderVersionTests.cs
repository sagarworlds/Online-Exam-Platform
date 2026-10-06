using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Reading a question as it was at an earlier version, which is how an attempt keeps what it began with (FR-7).</summary>
public class QuestionBankReaderVersionTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

    private readonly IQuestionRepository _questions = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();
    private readonly QuestionBankReader _reader;

    /// <summary>A question on its second version: the wording was changed after it was first saved.</summary>
    private readonly Question _question;

    public QuestionBankReaderVersionTests()
    {
        _reader = new QuestionBankReader(_questions, _books);
        _question = Question.Create("<p>Old wording</p>", [new NewQuestionOption("A", true), new NewQuestionOption("B", false)], Guid.NewGuid(), Now);
        var options = _question.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();
        _question.Revise("<p>New wording</p>", options, answered: false, allowsMultiple: false, Now.AddMinutes(1));

        _questions.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_question]);
        _questions.GetCurrentVersionNumbersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int> { [_question.Id] = 2 });
        _questions.GetVersionsAsync(Arg.Any<IReadOnlyCollection<(Guid, int)>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<(Guid QuestionId, int VersionNumber)>>()
                .SelectMany(w => _question.Versions.Where(v => v.QuestionId == w.QuestionId && v.VersionNumber == w.VersionNumber)).ToList());
        _books.GetChapterRefsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, ChapterRef>());
    }

    [Fact]
    public async Task GetAsync_ReportsTheVersionInForce()
    {
        var read = Assert.Single(await _reader.GetAsync([_question.Id], CancellationToken.None));

        Assert.Equal(2, read.VersionNumber);
        Assert.Contains("New wording", read.Text);
    }

    [Fact]
    public async Task AnEarlierVersion_IsReadAsItWas()
    {
        var read = Assert.Single(await _reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 1)], CancellationToken.None));

        Assert.Equal(1, read.VersionNumber);
        Assert.Contains("Old wording", read.Text);
        Assert.Equal(["A", "B"], read.Options.Select(o => o.Text));
        Assert.Equal([true, false], read.Options.Select(o => o.IsCorrect));
    }

    [Fact]
    public async Task TheCurrentVersion_OrNone_IsReadFromTheQuestionItself_WithoutLookingUpVersions()
    {
        var current = Assert.Single(await _reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 2)], CancellationToken.None));
        var unsaid = Assert.Single(await _reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, null)], CancellationToken.None));

        Assert.Contains("New wording", current.Text);
        Assert.Contains("New wording", unsaid.Text);
        Assert.Equal(2, unsaid.VersionNumber);
        await _questions.DidNotReceiveWithAnyArgs().GetVersionsAsync(default!, default);
    }

    [Fact]
    public async Task AVersionThatWasNeverStored_ReadsAsTheQuestionIsNow()
    {
        var read = Assert.Single(await _reader.GetVersionsAsync([new QuestionVersionRef(_question.Id, 9)], CancellationToken.None));

        Assert.Contains("New wording", read.Text);
        Assert.Equal(2, read.VersionNumber);
    }

    [Fact]
    public async Task NoQuestionsWanted_ReadsNothing() =>
        Assert.Empty(await _reader.GetVersionsAsync([], CancellationToken.None));
}
