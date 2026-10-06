using System.Text;
using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Import and export in every file format (FR-6): each is read into the same rows, so each is validated the same way.</summary>
public class QuestionFilesTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private static Question Sample(string text = "<p>Capital of France?</p>") =>
        Question.Create(text, [new NewQuestionOption("Paris", true), new NewQuestionOption("Rome", false)], Author, Now,
            chapterId: null, QuestionDifficulty.Easy, ["geography", "europe"], allowsMultiple: false);

    private static (ImportQuestionsHandler Handler, List<Question> Added) Importer()
    {
        var added = new List<Question>();
        var repository = Substitute.For<IQuestionRepository>();
        repository.When(r => r.Add(Arg.Any<Question>())).Do(c => added.Add(c.Arg<Question>()));
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(Now);
        return (new ImportQuestionsHandler(repository, Substitute.For<IQuestionBankUnitOfWork>(), new RichTextSanitizer(), clock), added);
    }

    [Theory]
    [InlineData(null, QuestionFileFormat.Csv)]
    [InlineData("", QuestionFileFormat.Csv)]
    [InlineData("CSV", QuestionFileFormat.Csv)]
    [InlineData("xlsx", QuestionFileFormat.Xlsx)]
    [InlineData("Excel", QuestionFileFormat.Xlsx)]
    [InlineData(" json ", QuestionFileFormat.Json)]
    public void ParseFormat_UnderstandsEachName(string? name, QuestionFileFormat expected) =>
        Assert.Equal(expected, QuestionFiles.ParseFormat(name));

    [Fact]
    public void ParseFormat_RefusesAnUnknownName() =>
        Assert.Throws<InvalidQuestionError>(() => QuestionFiles.ParseFormat("pdf"));

    [Theory]
    [InlineData(QuestionFileFormat.Csv)]
    [InlineData(QuestionFileFormat.Json)]
    [InlineData(QuestionFileFormat.Xlsx)]
    public async Task AnExportInAnyFormat_ImportsBackAsTheSameQuestion(QuestionFileFormat format)
    {
        var file = QuestionFiles.Write(format, [Sample()]);
        var content = format == QuestionFileFormat.Xlsx ? Convert.ToBase64String(file.Bytes) : Encoding.UTF8.GetString(file.Bytes);
        var (handler, added) = Importer();

        var result = await handler.HandleAsync(new ImportQuestionsCommand(content, Author, format), CancellationToken.None);

        Assert.Empty(result.Rejected);
        var question = Assert.Single(added);
        Assert.Contains("Capital of France?", question.Text);
        Assert.Equal(["Paris", "Rome"], question.Options.OrderBy(o => o.Order).Select(o => o.Text));
        Assert.Equal([true, false], question.Options.OrderBy(o => o.Order).Select(o => o.IsCorrect));
        Assert.Equal(QuestionDifficulty.Easy, question.Difficulty);
        Assert.Equal(["europe", "geography"], question.Topics.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Write_ReportsTheContentTypeAndFileNameOfEachFormat()
    {
        Assert.Equal(("text/csv", "questions.csv"), Pair(QuestionFiles.Write(QuestionFileFormat.Csv, [])));
        Assert.Equal(("application/json", "questions.json"), Pair(QuestionFiles.Write(QuestionFileFormat.Json, [])));
        Assert.Equal(("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "questions.xlsx"), Pair(QuestionFiles.Write(QuestionFileFormat.Xlsx, [])));

        static (string, string) Pair(ExportedQuestionFile f) => (f.ContentType, f.FileName);
    }

    [Fact]
    public void AWorkbook_LeavesOutAQuestionTooLongForACell_AndSaysSo()
    {
        var huge = Question.Create("<p>" + new string('x', Xlsx.MaxCellLength + 10) + "</p>",
            [new NewQuestionOption("A", true), new NewQuestionOption("B", false)], Author, Now);

        var workbook = QuestionFiles.Write(QuestionFileFormat.Xlsx, [Sample(), huge]);
        var json = QuestionFiles.Write(QuestionFileFormat.Json, [Sample(), huge]);

        Assert.Equal(1, workbook.Skipped);
        Assert.Equal(2, Xlsx.Read(workbook.Bytes).Count); // header and the one that fits
        Assert.Equal(0, json.Skipped);
    }

    [Fact]
    public async Task Json_ReportsABadObjectByItsPosition_AndImportsTheRest()
    {
        const string json = """
            [
              {"text":"Good one","options":[{"text":"A","isCorrect":true},{"text":"B"}]},
              42,
              {"text":"Too many","options":[{"text":"1"},{"text":"2"},{"text":"3"},{"text":"4"},{"text":"5"},{"text":"6"},{"text":"7"}]},
              {"text":"No answer marked","options":[{"text":"A"},{"text":"B"}]}
            ]
            """;
        var (handler, added) = Importer();

        var result = await handler.HandleAsync(new ImportQuestionsCommand(json, Author, QuestionFileFormat.Json), CancellationToken.None);

        Assert.Equal([1], result.Created.Select(c => c.Row));
        Assert.Equal([2, 3, 4], result.Rejected.Select(r => r.Row));
        Assert.Contains("JSON object", result.Rejected[0].Errors[0]);
        Assert.Contains("at most 6", result.Rejected[1].Errors[0]);
        Assert.Single(added);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"text\":\"an object, not an array\"}")]
    public void Json_ThatIsNotAnArrayOfQuestions_IsUnreadable(string json) =>
        Assert.Throws<BulkImportUnreadableError>(() => QuestionFiles.Read(QuestionFileFormat.Json, json));

    [Theory]
    [InlineData("%%% not base64 %%%")]
    [InlineData("aGVsbG8=")] // valid base64, but not a workbook
    public void Excel_ThatIsNotAWorkbook_IsUnreadable(string content) =>
        Assert.Throws<BulkImportUnreadableError>(() => QuestionFiles.Read(QuestionFileFormat.Xlsx, content));

    [Fact]
    public async Task AWorkbookRow_IsReportedByItsLineInTheSheet()
    {
        var header = QuestionCsvRowHeader();
        var good = new[] { "Q1?", "A", "true", "B", "false", "", "", "", "", "", "", "", "", "false", "", "" };
        var bad = new[] { "", "A", "true", "B", "false", "", "", "", "", "", "", "", "", "false", "", "" };
        var content = Convert.ToBase64String(Xlsx.Write([header, good, bad]));
        var (handler, _) = Importer();

        var result = await handler.HandleAsync(new ImportQuestionsCommand(content, Author, QuestionFileFormat.Xlsx), CancellationToken.None);

        Assert.Equal([2], result.Created.Select(c => c.Row));
        Assert.Equal([3], result.Rejected.Select(r => r.Row));
    }

    private static string[] QuestionCsvRowHeader() =>
    [
        "Text", "Option1", "Correct1", "Option2", "Correct2", "Option3", "Correct3", "Option4", "Correct4",
        "Option5", "Correct5", "Option6", "Correct6", "AllowsMultiple", "Difficulty", "Topics",
    ];
}
