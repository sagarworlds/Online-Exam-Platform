using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class ExportQuestionsHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly ExportQuestionsHandler handler;

    public ExportQuestionsHandlerTests()
    {
        handler = new ExportQuestionsHandler(repository);
    }

    private static Question TwoOptionQuestion(string text = "Capital of France?") =>
        Question.Create(text, [new NewQuestionOption("Paris", true), new NewQuestionOption("Rome", false)], Author, Now);

    [Fact]
    public async Task Export_WithNoQuestions_IsJustTheHeader()
    {
        repository.ListNewestAsync(Arg.Any<QuestionFilter>(), 0, ExportQuestionsHandler.MaxRows, Arg.Any<CancellationToken>())
            .Returns([]);

        var csv = await handler.HandleAsync(new QuestionFilter(), CancellationToken.None);
        var rows = Csv.Parse(csv);

        Assert.Single(rows);
        Assert.Equal("Text", rows[0][0]);
    }

    [Fact]
    public async Task Export_WritesOneDataRowPerQuestion_WithItsTextAndOptions()
    {
        var question = TwoOptionQuestion();
        repository.ListNewestAsync(Arg.Any<QuestionFilter>(), 0, ExportQuestionsHandler.MaxRows, Arg.Any<CancellationToken>())
            .Returns([question]);

        var csv = await handler.HandleAsync(new QuestionFilter(), CancellationToken.None);
        var rows = Csv.Parse(csv);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Capital of France?", rows[1][0]);
        Assert.Equal("Paris", rows[1][1]);
        Assert.Equal("true", rows[1][2]);
        Assert.Equal("Rome", rows[1][3]);
        Assert.Equal("false", rows[1][4]);
    }

    [Fact]
    public async Task Export_ThenImport_RoundTripsTheQuestionUnchanged()
    {
        var question = TwoOptionQuestion("<p>Capital of France?</p>");
        repository.ListNewestAsync(Arg.Any<QuestionFilter>(), 0, ExportQuestionsHandler.MaxRows, Arg.Any<CancellationToken>())
            .Returns([question]);

        var csv = await handler.HandleAsync(new QuestionFilter(), CancellationToken.None);

        var unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
        var importRepository = Substitute.For<IQuestionRepository>();
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(Now);
        var importHandler = new ImportQuestionsHandler(importRepository, unitOfWork, new RichTextSanitizer(), clock);

        var result = await importHandler.HandleAsync(new ImportQuestionsCommand(csv, Author), CancellationToken.None);

        Assert.Empty(result.Rejected);
        Assert.Equal(1, result.Created.Count);
        importRepository.Received(1).Add(Arg.Is<Question>(q => q.Text == question.Text && q.Options.Count == 2));
    }

    [Fact]
    public async Task Export_PassesTheFilterThrough()
    {
        var filter = new QuestionFilter(BookId: Guid.NewGuid(), Difficulty: QuestionDifficulty.Hard);
        repository.ListNewestAsync(filter, 0, ExportQuestionsHandler.MaxRows, Arg.Any<CancellationToken>()).Returns([]);

        await handler.HandleAsync(filter, CancellationToken.None);

        await repository.Received(1).ListNewestAsync(filter, 0, ExportQuestionsHandler.MaxRows, Arg.Any<CancellationToken>());
    }
}
