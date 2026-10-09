using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class ImportQuestionsHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly ImportQuestionsHandler handler;

    public ImportQuestionsHandlerTests()
    {
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(Now);
        handler = new ImportQuestionsHandler(repository, new QuestionDuplicateFinder(repository), new QuestionDuplicatePolicy(true), unitOfWork, new RichTextSanitizer(), clock);
    }

    // Mirrors QuestionCsvRow.Header (internal to the Application project, so not referenced directly here):
    // Text,Option1,Correct1,...,Option6,Correct6,AllowsMultiple,Difficulty,Topics.
    private static readonly string[] Header =
    [
        "Text", "Option1", "Correct1", "Option2", "Correct2", "Option3", "Correct3",
        "Option4", "Correct4", "Option5", "Correct5", "Option6", "Correct6",
        "AllowsMultiple", "Difficulty", "Topics",
    ];

    private static string Row(string text, string option1 = "Paris", string correct1 = "true", string option2 = "Rome", string correct2 = "false") =>
        Csv.WriteRow([text, option1, correct1, option2, correct2, "", "", "", "", "", "", "", "", "false", "", ""]);

    private static string FileOf(params string[] dataRows) =>
        Csv.WriteRow(Header) + string.Concat(dataRows);

    [Fact]
    public async Task Import_WithOneGoodRow_CreatesTheQuestionAndReportsItsLine()
    {
        var result = await handler.HandleAsync(new ImportQuestionsCommand(FileOf(Row("Capital of France?")), Author), CancellationToken.None);

        Assert.Empty(result.Rejected);
        Assert.Equal(1, result.Created.Count);
        Assert.Equal(2, result.Created[0].Row);
        repository.Received(1).Add(Arg.Is<Question>(q => q.Text.Contains("Capital of France?")));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_WithABadRowAmongGoodOnes_SkipsOnlyTheBadRow()
    {
        var file = FileOf(Row("Capital of France?"), Row(""), Row("Capital of Italy?", "Rome", "true", "Paris", "false"));

        var result = await handler.HandleAsync(new ImportQuestionsCommand(file, Author), CancellationToken.None);

        Assert.Equal(2, result.Created.Count);
        Assert.Equal([2, 4], result.Created.Select(c => c.Row).ToArray());
        Assert.Equal(1, result.Rejected.Count);
        Assert.Equal(3, result.Rejected[0].Row);
        repository.Received(2).Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Import_WithEveryRowBad_CreatesNothingAndDoesNotSave()
    {
        var result = await handler.HandleAsync(new ImportQuestionsCommand(FileOf(Row("")), Author), CancellationToken.None);

        Assert.Empty(result.Created);
        Assert.Equal(1, result.Rejected.Count);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_WithAWrongColumnCount_RejectsTheRowWithoutThrowing()
    {
        var file = Csv.WriteRow(Header) + Csv.WriteRow(["Capital of France?", "Paris", "true"]);

        var result = await handler.HandleAsync(new ImportQuestionsCommand(file, Author), CancellationToken.None);

        Assert.Empty(result.Created);
        Assert.Equal(1, result.Rejected.Count);
    }

    [Fact]
    public async Task Import_WithMoreRowsThanTheLimit_IsRefusedOutright()
    {
        var rows = Enumerable.Range(0, ImportQuestionsHandler.MaxRows + 1).Select(_ => Row("Capital of France?")).ToArray();

        var error = await Assert.ThrowsAsync<BulkImportTooLargeError>(
            () => handler.HandleAsync(new ImportQuestionsCommand(FileOf(rows), Author), CancellationToken.None));

        Assert.Equal(400, error.HttpStatusCode);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Import_StoresTheSanitizedHtmlNotWhatWasSent()
    {
        var file = FileOf(Row("<p>Capital of France?<script>alert(1)</script></p>"));

        await handler.HandleAsync(new ImportQuestionsCommand(file, Author), CancellationToken.None);

        repository.Received(1).Add(Arg.Is<Question>(q => q.Text == "<p>Capital of France?</p>"));
    }

    [Fact]
    public async Task Import_WithFewerThanTheMaximumOptions_LeavesTheUnusedColumnsOut()
    {
        var file = FileOf(Row("Capital of France?"));

        await handler.HandleAsync(new ImportQuestionsCommand(file, Author), CancellationToken.None);

        repository.Received(1).Add(Arg.Is<Question>(q => q.Options.Count == 2));
    }
}
