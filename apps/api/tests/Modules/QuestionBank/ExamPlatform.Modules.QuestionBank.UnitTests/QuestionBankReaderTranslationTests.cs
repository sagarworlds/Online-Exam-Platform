using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Finding the translation to show a candidate who prefers another language (FR-51).</summary>
public class QuestionBankReaderTranslationTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IQuestionRepository _questions = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository _books = Substitute.For<IBookRepository>();

    private readonly Question _english = Question.Create("<p>Capital of France?</p>", [new("Paris", true), new("Rome", false)], Author, Now);

    private Question Translate(string language, string text, string first, string second, bool approved = false)
    {
        var translation = Question.Create(text, [new(first, true), new(second, false)], Author, Now, language: language, translationGroupId: _english.TranslationGroupId);
        if (approved)
        {
            translation.SubmitForReview(Author, null, null, Now);
            translation.Approve(Author, null, null, Now);
        }

        return translation;
    }

    private QuestionBankReader Reader(bool requireApproval, params Question[] bankHolds)
    {
        _questions.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Question>>(bankHolds.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _questions.ListTranslationCandidatesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyCollection<QuestionStatus>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Question>>(bankHolds
                .Where(q => call.ArgAt<IReadOnlyCollection<Guid>>(0).Contains(q.TranslationGroupId)
                    && call.ArgAt<IReadOnlyCollection<string>>(1).Contains(q.Language)
                    && call.ArgAt<IReadOnlyCollection<QuestionStatus>>(2).Contains(q.Status)).ToList()));
        return new QuestionBankReader(_questions, _books, new QuestionApprovalPolicy(requireApproval));
    }

    [Fact]
    public async Task AQuestionIsShownInTheFirstLanguageWantedThatItHas()
    {
        var hindi = Translate("hi", "<p>फ्रांस की राजधानी?</p>", "पेरिस", "रोम");
        var marathi = Translate("mr", "<p>फ्रान्सची राजधानी?</p>", "पॅरिस", "रोम");
        var reader = Reader(false, _english, hindi, marathi);

        var found = Assert.Single(await reader.GetTranslationsAsync([_english.Id], ["mr", "hi", "en"], CancellationToken.None));

        Assert.Equal((_english.Id, "mr", "<p>फ्रान्सची राजधानी?</p>"), (found.QuestionId, found.Language, found.Text));
        Assert.Equal(["पॅरिस", "रोम"], found.OptionTexts);
    }

    [Fact]
    public async Task AQuestionAlreadyInTheLanguageWantedMost_IsShownAsItIs()
    {
        var hindi = Translate("hi", "<p>फ्रांस की राजधानी?</p>", "पेरिस", "रोम");

        // English is wanted before Hindi, and the question is English: Hindi is only a fallback for questions that lack English.
        Assert.Empty(await Reader(false, _english, hindi).GetTranslationsAsync([_english.Id], ["en", "hi"], CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheQuestionHasNoTranslationInAnyWantedLanguage_NothingIsReturned()
    {
        var marathi = Translate("mr", "<p>फ्रान्सची राजधानी?</p>", "पॅरिस", "रोम");

        Assert.Empty(await Reader(false, _english, marathi).GetTranslationsAsync([_english.Id], ["hi"], CancellationToken.None));
    }

    [Fact]
    public async Task ATranslationCanBeReadAsTheQuestion_WhenTheExamHoldsTheTranslationAndTheCandidateWantsTheOriginal()
    {
        var hindi = Translate("hi", "<p>फ्रांस की राजधानी?</p>", "पेरिस", "रोम");

        var found = Assert.Single(await Reader(false, _english, hindi).GetTranslationsAsync([hindi.Id], ["en"], CancellationToken.None));

        Assert.Equal((hindi.Id, "en"), (found.QuestionId, found.Language));
        Assert.Equal(["Paris", "Rome"], found.OptionTexts);
    }

    [Fact]
    public async Task WhereApprovalIsRequired_AnUnapprovedTranslationIsNotShownToCandidates()
    {
        var draft = Translate("hi", "<p>मसौदा</p>", "पेरिस", "रोम");
        var approved = Translate("mr", "<p>मंजूर</p>", "पॅरिस", "रोम", approved: true);
        var reader = Reader(true, _english, draft, approved);

        var found = Assert.Single(await reader.GetTranslationsAsync([_english.Id], ["hi", "mr"], CancellationToken.None));

        Assert.Equal("mr", found.Language);
    }

    [Fact]
    public async Task ARetiredTranslation_IsNeverShown()
    {
        var hindi = Translate("hi", "<p>फ्रांस की राजधानी?</p>", "पेरिस", "रोम");
        hindi.Retire(Author, null, null, Now);

        Assert.Empty(await Reader(false, _english, hindi).GetTranslationsAsync([_english.Id], ["hi"], CancellationToken.None));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("xx")]
    public async Task ALanguageTheBankDoesNotSupport_IsIgnored_WithoutAskingTheStore(string language)
    {
        Assert.Empty(await Reader(false, _english).GetTranslationsAsync([_english.Id], [language], CancellationToken.None));

        await _questions.DidNotReceiveWithAnyArgs().ListTranslationCandidatesAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task NoQuestionsOrNoLanguages_AsksNothing()
    {
        var reader = Reader(false, _english);

        Assert.Empty(await reader.GetTranslationsAsync([], ["hi"], CancellationToken.None));
        Assert.Empty(await reader.GetTranslationsAsync([_english.Id], [], CancellationToken.None));
        await _questions.DidNotReceiveWithAnyArgs().GetManyAsync(default!, default);
    }
}
