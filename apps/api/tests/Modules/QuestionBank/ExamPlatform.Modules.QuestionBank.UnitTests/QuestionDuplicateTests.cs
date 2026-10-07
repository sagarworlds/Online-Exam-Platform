using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Duplicate detection (FR-9): what makes two questions the same, and what the bank does when one is added twice.</summary>
public class QuestionDuplicateTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository books = Substitute.For<IBookRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    public QuestionDuplicateTests() => clock.UtcNow.Returns(Now);

    private CreateQuestionHandler Creator(bool refuse = true) =>
        new(repository, new OpenChapterResolver(books, Substitute.For<IClassRepository>()), new QuestionDuplicateFinder(repository), new QuestionDuplicatePolicy(refuse), unitOfWork, new RichTextSanitizer(), clock);

    private ImportQuestionsHandler Importer(bool refuse = true) =>
        new(repository, new QuestionDuplicateFinder(repository), new QuestionDuplicatePolicy(refuse), unitOfWork, new RichTextSanitizer(), clock);

    /// <summary>A question already in the bank, with its key set the way storing it would.</summary>
    private static Question Stored(string text, params (string Text, bool Correct)[] options)
    {
        var question = Question.Create(
            $"<p>{text}</p>", options.Select(o => new NewQuestionOption(o.Text, o.Correct)).ToList(), Author, Now, null, null, null, false);
        question.IndexText(text);
        return question;
    }

    private void BankHolds(params Question[] questions) =>
        repository.FindByTextKeyAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Question>>(
                questions.Where(q => q.TextKey == (string)call.Args()[0] && q.Id != (Guid?)call.Args()[1]).ToList()));

    private static List<NewQuestionOption> Paris() => [new("Paris", true), new("Rome", false)];

    [Theory]
    [InlineData("What is 2 + 2?", "what is  2+2")]
    [InlineData("Capital of <b>France</b>?", "Capital of <b>France</b>?")]
    [InlineData("ÉCOLE", "école")]
    public void TheSameWordsInAnotherCaseSpacingOrPunctuation_AreTheSameKey(string a, string b) =>
        Assert.Equal(QuestionFingerprint.KeyOf(Strip(a)), QuestionFingerprint.KeyOf(Strip(b)));

    private static string Strip(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty);

    [Fact]
    public void DifferentWords_AreADifferentKey() =>
        Assert.NotEqual(QuestionFingerprint.KeyOf("Capital of France?"), QuestionFingerprint.KeyOf("Capital of Italy?"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ?! ")]
    public void NothingReadable_HasNoKey_SoPictureOnlyQuestionsAreNeverTakenForEachOther(string? text) =>
        Assert.Equal(string.Empty, QuestionFingerprint.KeyOf(text));

    [Fact]
    public void TheSameOptionsInAnotherOrder_AreTheSameOptions() =>
        Assert.Equal(QuestionFingerprint.OptionsKeyOf(["Paris", "Rome"]), QuestionFingerprint.OptionsKeyOf([" rome ", "PARIS"]));

    [Fact]
    public void AQuestionKeepsItsKeyWithItsSearchText()
    {
        var question = Stored("Capital of France?", ("Paris", true), ("Rome", false));

        Assert.Equal(QuestionFingerprint.KeyOf("capital of france"), question.TextKey);
    }

    [Fact]
    public async Task Create_RefusesAQuestionTheBankAlreadyHas()
    {
        BankHolds(Stored("Capital of France?", ("Paris", true), ("Rome", false)));

        var error = await Assert.ThrowsAsync<DuplicateQuestionError>(
            () => Creator().HandleAsync(new CreateQuestionCommand("<p>capital of  France</p>", Paris(), Author), CancellationToken.None));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal("duplicate_question", error.ErrorCode);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Create_AddsTheDuplicateWhenTheAuthorSaysSo()
    {
        BankHolds(Stored("Capital of France?", ("Paris", true), ("Rome", false)));

        await Creator().HandleAsync(new CreateQuestionCommand("<p>Capital of France?</p>", Paris(), Author, AllowDuplicate: true), CancellationToken.None);

        repository.Received(1).Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Create_AllowsTheSameWordingWithOtherOptions()
    {
        BankHolds(Stored("Capital of France?", ("Paris", true), ("Rome", false)));

        await Creator().HandleAsync(
            new CreateQuestionCommand("<p>Capital of France?</p>", [new("Lyon", false), new("Paris", true), new("Nice", false)], Author), CancellationToken.None);

        repository.Received(1).Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Create_ChecksNothingWhereTheDeploymentAcceptsRepeats()
    {
        BankHolds(Stored("Capital of France?", ("Paris", true), ("Rome", false)));

        await Creator(refuse: false).HandleAsync(new CreateQuestionCommand("<p>Capital of France?</p>", Paris(), Author), CancellationToken.None);

        repository.Received(1).Add(Arg.Any<Question>());
        await repository.DidNotReceiveWithAnyArgs().FindByTextKeyAsync(default!, default, default, default);
    }

    private static string Row(string text, string first = "Paris", string second = "Rome") =>
        Csv.WriteRow([text, first, "true", second, "false", "", "", "", "", "", "", "", "", "false", "", ""]);

    private static string FileOf(params string[] rows) =>
        Csv.WriteRow(["Text", "Option1", "Correct1", "Option2", "Correct2", "Option3", "Correct3", "Option4", "Correct4", "Option5", "Correct5", "Option6", "Correct6", "AllowsMultiple", "Difficulty", "Topics"])
        + string.Concat(rows);

    [Fact]
    public async Task Import_LeavesOutARowTheBankHas_AndSaysWhy()
    {
        var existing = Stored("Capital of France?", ("Paris", true), ("Rome", false));
        BankHolds(existing);

        var result = await Importer().HandleAsync(new ImportQuestionsCommand(FileOf(Row("Capital of France?"), Row("Capital of Italy?", "Rome", "Paris")), Author), CancellationToken.None);

        Assert.Equal([3], result.Created.Select(c => c.Row));
        var skipped = Assert.Single(result.Duplicates);
        Assert.Equal(2, skipped.Row);
        Assert.Contains(existing.Id.ToString(), skipped.Reason);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public async Task Import_LeavesOutARowThatRepeatsAnEarlierRowOfTheSameFile()
    {
        var result = await Importer().HandleAsync(new ImportQuestionsCommand(FileOf(Row("Capital of France?"), Row("capital of france"), Row("Capital of Italy?", "Rome", "Paris")), Author), CancellationToken.None);

        Assert.Equal([2, 4], result.Created.Select(c => c.Row));
        var skipped = Assert.Single(result.Duplicates);
        Assert.Equal((3, "Same as row 2 of this file."), (skipped.Row, skipped.Reason));
    }

    [Fact]
    public async Task Import_CreatesRepeatsWhenAskedTo()
    {
        BankHolds(Stored("Capital of France?", ("Paris", true), ("Rome", false)));

        var result = await Importer().HandleAsync(new ImportQuestionsCommand(FileOf(Row("Capital of France?"), Row("Capital of France?")), Author, AllowDuplicates: true), CancellationToken.None);

        Assert.Equal(2, result.Created.Count);
        Assert.Empty(result.Duplicates);
    }

    [Fact]
    public async Task TheCheck_ListsSameOptionsFirst_AndSkipsTheQuestionBeingEdited()
    {
        var other = Stored("Capital of France?", ("Lyon", true), ("Nice", false));
        var same = Stored("Capital of France?", ("Rome", false), ("Paris", true));
        var self = Stored("Capital of France?", ("Paris", true), ("Rome", false));
        BankHolds(other, same, self);
        var handler = new FindDuplicatesHandler(new QuestionDuplicateFinder(repository), new RichTextSanitizer());

        var found = await handler.HandleAsync("<p>Capital of France?</p>", ["Paris", "Rome"], self.Id, CancellationToken.None);

        Assert.Equal([same.Id, other.Id], found.Select(f => f.Id));
        Assert.Equal([true, false], found.Select(f => f.SameOptions));
        Assert.Equal("Capital of France?", found[0].Preview);
    }

    [Fact]
    public async Task TheCheck_FindsNothingForTextWithNothingReadable()
    {
        BankHolds(Stored("Capital of France?", ("Paris", true), ("Rome", false)));
        var handler = new FindDuplicatesHandler(new QuestionDuplicateFinder(repository), new RichTextSanitizer());

        Assert.Empty(await handler.HandleAsync("<p> </p>", ["Paris", "Rome"], null, CancellationToken.None));
    }

    [Fact]
    public async Task Statistics_ReportThePercentCorrect_AndHowOftenEachOptionWasChosen()
    {
        var question = Stored("Capital of France?", ("Paris", true), ("Rome", false));
        repository.GetByIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns(question);
        var paris = question.Options.Single(o => o.IsCorrect).Id;
        var source = Substitute.For<IQuestionStatisticsSource>();
        source.ReadAsync(question.Id, Arg.Any<CancellationToken>())
            .Returns(new QuestionAnswerStatistics(3, 1, new Dictionary<Guid, int> { [paris] = 1 }));
        var handler = new GetQuestionStatisticsHandler(repository, new QuestionUsageReader([]), source);

        var stats = await handler.HandleAsync(question.Id, CancellationToken.None);

        Assert.Equal((3, 1, 33.3m), (stats.Answered, stats.Correct, stats.PercentCorrect));
        Assert.Equal([1, 0], stats.Options.Select(o => o.TimesChosen));
        Assert.Equal(0, stats.ExamCount);
    }

    [Fact]
    public async Task Statistics_OfAQuestionNobodyAnswered_HaveNoPercent()
    {
        var question = Stored("Capital of France?", ("Paris", true), ("Rome", false));
        repository.GetByIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns(question);
        var source = Substitute.For<IQuestionStatisticsSource>();
        source.ReadAsync(question.Id, Arg.Any<CancellationToken>()).Returns(new QuestionAnswerStatistics(0, 0, new Dictionary<Guid, int>()));

        var stats = await new GetQuestionStatisticsHandler(repository, new QuestionUsageReader([]), source).HandleAsync(question.Id, CancellationToken.None);

        Assert.Null(stats.PercentCorrect);
    }

    [Fact]
    public async Task Statistics_OfAnUnknownQuestion_AreNotFound() =>
        await Assert.ThrowsAsync<QuestionNotFoundError>(() =>
            new GetQuestionStatisticsHandler(repository, new QuestionUsageReader([]), Substitute.For<IQuestionStatisticsSource>())
                .HandleAsync(Guid.NewGuid(), CancellationToken.None));
}
