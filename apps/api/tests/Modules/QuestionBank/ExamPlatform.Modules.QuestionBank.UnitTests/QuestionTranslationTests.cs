using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Multi-language content (FR-10): a question has a language, and a translation is a linked question that cannot disagree with its source about what is right.</summary>
public class QuestionTranslationTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Chapter = Guid.NewGuid();

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository books = Substitute.For<IBookRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly Clock clock = Substitute.For<Clock>();

    public QuestionTranslationTests() => clock.UtcNow.Returns(Now);

    private AddTranslationHandler Translator() =>
        new(repository, new QuestionDtoFactory(books), unitOfWork, new RichTextSanitizer(), clock);

    /// <summary>A hard, multiple-answer English question filed under a chapter, with the second and third options correct and the last one pinned.</summary>
    private static Question Source() => Question.Create(
        "<p>Pick the primes</p>",
        [new("Four", false), new("Three", true), new("Five", true), new("None of these", false, IsPinned: true)],
        Author, Now, Chapter, QuestionDifficulty.Hard, ["Primes"], allowsMultiple: true);

    private Question SourceInTheBank(params Question[] alsoInTheGroup)
    {
        var source = Source();
        repository.GetByIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        repository.ListTranslationGroupAsync(source.TranslationGroupId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Question>>([source, .. alsoInTheGroup]));
        return source;
    }

    private static AddTranslationCommand Hindi(Guid sourceId, string? language = "hi", IReadOnlyList<string?>? options = null) =>
        new(sourceId, language, "<p>अभाज्य संख्याएँ चुनिए</p>", options ?? ["चार", "तीन", "पाँच", "इनमें से कोई नहीं"], Author);

    [Theory]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData("  ", "en")]
    [InlineData("hi", "hi")]
    [InlineData(" HI ", "hi")]
    [InlineData("MR", "mr")]
    public void ALanguageIsReadIgnoringCaseAndSpaces_AndBlankMeansEnglish(string? text, string expected) =>
        Assert.Equal(expected, QuestionLanguage.Parse(text));

    [Fact]
    public void ALanguageTheBankDoesNotSupport_IsRefused() =>
        Assert.Contains("en, hi, mr", Assert.Throws<InvalidQuestionError>(() => QuestionLanguage.Parse("fr")).Message);

    [Fact]
    public void AQuestionIsEnglishAndAloneInItsOwnGroup_UnlessSaidOtherwise()
    {
        var question = Question.Create("<p>Q</p>", [new("A", true), new("B", false)], Author, Now);

        Assert.Equal("en", question.Language);
        Assert.Equal(question.Id, question.TranslationGroupId);
    }

    [Fact]
    public void AQuestionCanBeWrittenInHindi_AndAnotherQuestionsGroupJoined()
    {
        var group = Guid.NewGuid();

        var question = Question.Create("<p>प्र</p>", [new("क", true), new("ख", false)], Author, Now, language: "hi", translationGroupId: group);

        Assert.Equal(("hi", group), (question.Language, question.TranslationGroupId));
    }

    [Fact]
    public void HindiWordsThatDifferOnlyByAVowelSign_AreNotTheSameQuestion() =>
        // The vowel signs are combining marks, not letters; a key that dropped them would call these the same.
        Assert.NotEqual(QuestionFingerprint.KeyOf("की"), QuestionFingerprint.KeyOf("के"));

    [Fact]
    public async Task ATranslationIsADraftInTheSourcesGroup_WithTheWordsItWasGiven()
    {
        var source = SourceInTheBank();

        var dto = await Translator().HandleAsync(Hindi(source.Id), CancellationToken.None);

        Assert.Equal(("hi", source.TranslationGroupId, "draft"), (dto.Language, dto.TranslationGroupId, dto.Status));
        Assert.NotEqual(source.Id, dto.Id);
        Assert.Contains("अभाज्य संख्याएँ चुनिए", dto.Text);
        Assert.Equal(["चार", "तीन", "पाँच", "इनमें से कोई नहीं"], dto.Options.Select(o => o.Text));
        repository.Received(1).Add(Arg.Is<Question>(q => q.Id == dto.Id && q.SearchText.Contains("अभाज्य")));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATranslationTakesTheAnswerKeyAndEverythingElseFromTheSource_SoTheTwoCannotDisagree()
    {
        var source = SourceInTheBank();

        var dto = await Translator().HandleAsync(Hindi(source.Id), CancellationToken.None);

        Assert.Equal([false, true, true, false], dto.Options.Select(o => o.IsCorrect));
        Assert.Equal([false, false, false, true], dto.Options.Select(o => o.IsPinned));
        Assert.True(dto.AllowsMultiple);
        Assert.Equal("hard", dto.Difficulty);
        Assert.Equal(["primes"], dto.Topics);
        // Where it is filed is read from the stored question: the response's book and chapter names come from a lookup this test does not stub.
        repository.Received(1).Add(Arg.Is<Question>(q => q.Id == dto.Id && q.ChapterId == Chapter));
    }

    [Fact]
    public async Task ATranslationDoesNotReuseTheSourcesOptionIds()
    {
        var source = SourceInTheBank();

        var dto = await Translator().HandleAsync(Hindi(source.Id), CancellationToken.None);

        // A saved answer points at an option id; sharing ids would make an answer to one question an answer to the other.
        Assert.Empty(dto.Options.Select(o => o.Id).Intersect(source.Options.Select(o => o.Id)));
    }

    [Fact]
    public async Task ASecondTranslationInTheSameLanguage_IsRefused()
    {
        var existing = Question.Create("<p>x</p>", [new("a", true), new("b", false)], Author, Now, language: "hi");
        var source = SourceInTheBank(existing);

        var refused = await Assert.ThrowsAsync<TranslationExistsError>(() => Translator().HandleAsync(Hindi(source.Id), CancellationToken.None));

        Assert.Equal("translation_exists", refused.ErrorCode);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task ATranslationIntoTheSourcesOwnLanguage_IsRefused()
    {
        var source = SourceInTheBank();

        await Assert.ThrowsAsync<TranslationExistsError>(() => Translator().HandleAsync(Hindi(source.Id, "en"), CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    public async Task ATranslationNeedsASupportedLanguage_WithNoDefault(string? language)
    {
        var source = SourceInTheBank();

        await Assert.ThrowsAsync<InvalidQuestionError>(() => Translator().HandleAsync(Hindi(source.Id, language), CancellationToken.None));

        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ATranslationNeedsOneOptionForEachOptionOfTheSource(int count)
    {
        var source = SourceInTheBank();

        var refused = await Assert.ThrowsAsync<InvalidQuestionError>(() =>
            Translator().HandleAsync(Hindi(source.Id, options: Enumerable.Repeat<string?>("विकल्प", count).ToList()), CancellationToken.None));

        Assert.Contains("4 options", refused.Message);
    }

    [Fact]
    public async Task ATranslationWithABlankOption_IsRefused()
    {
        var source = SourceInTheBank();

        await Assert.ThrowsAsync<InvalidQuestionError>(() =>
            Translator().HandleAsync(Hindi(source.Id, options: ["चार", " ", "पाँच", "कोई नहीं"]), CancellationToken.None));
    }

    [Fact]
    public async Task ATranslationOfAQuestionThatIsNotThere_IsNotFound() =>
        await Assert.ThrowsAsync<QuestionNotFoundError>(() => Translator().HandleAsync(Hindi(Guid.NewGuid()), CancellationToken.None));

    [Fact]
    public async Task TheTranslationsOfAQuestion_AreListedInTheOrderLanguagesAreOffered_ItselfIncluded()
    {
        var english = Question.Create("<p>Capital of France?</p>", [new("Paris", true), new("Rome", false)], Author, Now);
        english.IndexText("Capital of France?");
        var hindi = Question.Create("<p>फ्रांस की राजधानी?</p>", [new("पेरिस", true), new("रोम", false)], Author, Now.AddMinutes(1), language: "hi", translationGroupId: english.TranslationGroupId);
        hindi.IndexText("फ्रांस की राजधानी?");
        repository.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Question>>([hindi]));
        repository.ListTranslationGroupAsync(english.TranslationGroupId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Question>>([hindi, english]));

        var listed = await new ListTranslationsHandler(repository).HandleAsync(hindi.Id, CancellationToken.None);

        Assert.Equal([("en", english.Id), ("hi", hindi.Id)], listed.Select(t => (t.Language, t.Id)));
        Assert.Equal("Capital of France?", listed[0].Preview);
        Assert.Equal("draft", listed[0].Status);
    }

    [Fact]
    public async Task ListingTheTranslationsOfAQuestionThatIsNotThere_IsNotFound() =>
        await Assert.ThrowsAsync<QuestionNotFoundError>(() => new ListTranslationsHandler(repository).HandleAsync(Guid.NewGuid(), CancellationToken.None));
}
