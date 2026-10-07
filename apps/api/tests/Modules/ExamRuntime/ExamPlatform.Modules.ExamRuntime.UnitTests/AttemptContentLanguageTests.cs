using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// A candidate sees a question in the language they asked for where it has a translation (FR-51), and only the words change: the options
/// keep their ids and the key stays the question's, so what is saved and how it is marked never depend on the language.
/// </summary>
public class AttemptContentLanguageTests
{
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IRequestLanguage _language = Substitute.For<IRequestLanguage>();
    private readonly QuestionSnapshot _question = Fixtures.Question("Capital of France?");
    private readonly ExamSnapshot _exam;

    public AttemptContentLanguageTests()
    {
        _exam = Fixtures.Exam([_question]);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_question]));
        _language.Preferred.Returns(["hi"]);
    }

    private void BankTranslates(params string[] optionTexts) =>
        _bank.GetTranslationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionTranslationSnapshot>>([new QuestionTranslationSnapshot(_question.Id, "hi", "<p>फ्रांस की राजधानी?</p>", optionTexts)]));

    private Attempt Open() => Attempt.Start(_exam.Id, Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));

    private async Task<AttemptQuestionDto> ShownAsync(IRequestLanguage? language)
    {
        var dto = await new AttemptViewBuilder(_bank, _clock, language).BuildAsync(Open(), _exam, CancellationToken.None);
        return dto.Sections.Single().Questions.Single();
    }

    [Fact]
    public async Task ATranslatedQuestion_IsShownInTheLanguageAsked_WithTheSameOptionIdsInTheSamePlaces()
    {
        BankTranslates("चार", "पाँच", "बाईस");

        var shown = await ShownAsync(_language);

        Assert.Equal("<p>फ्रांस की राजधानी?</p>", shown.Text);
        // Same ids, in the same order (no shuffle on a first attempt), with the translated words on them.
        Assert.Equal(_question.Options.Select(o => o.Id), shown.Options.Select(o => o.Id));
        Assert.Equal(["चार", "पाँच", "बाईस"], shown.Options.Select(o => o.Text));
    }

    [Fact]
    public async Task WithNoLanguageAsked_TheBankIsNotAskedForTranslations_AndTheQuestionIsShownAsItIs()
    {
        var shown = await ShownAsync(null);

        Assert.Equal("Capital of France?", shown.Text);
        await _bank.DidNotReceiveWithAnyArgs().GetTranslationsAsync(default!, default!, default);
    }

    [Fact]
    public async Task WhenTheLanguageHasNoTranslation_TheQuestionIsShownAsItIs()
    {
        _bank.GetTranslationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionTranslationSnapshot>>([]));

        var shown = await ShownAsync(_language);

        Assert.Equal("Capital of France?", shown.Text);
        Assert.Equal(["4", "5", "22"], shown.Options.Select(o => o.Text));
    }

    [Fact]
    public async Task ATranslationWithAnotherNumberOfOptions_IsNotUsed_BecauseItsOptionsWouldNotLineUp()
    {
        BankTranslates("चार", "पाँच");

        var shown = await ShownAsync(_language);

        Assert.Equal("Capital of France?", shown.Text);
        Assert.Equal(["4", "5", "22"], shown.Options.Select(o => o.Text));
    }

    [Fact]
    public async Task TheAskedLanguagesArePassedToTheBankInOrder()
    {
        _language.Preferred.Returns(["mr", "hi"]);

        await ShownAsync(_language);

        await _bank.Received(1).GetTranslationsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "mr", "hi" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheReview_ShowsTheTranslation_ButMarksByTheOriginalKey()
    {
        BankTranslates("चार", "पाँच", "बाईस");
        var attempt = Open();
        attempt.RecordAnswer(_question.Id, _question.Correct(), Fixtures.Now);
        var result = AttemptScorer.Score(_exam, new Dictionary<Guid, QuestionSnapshot> { [_question.Id] = _question }, attempt.Answers.ToList());
        attempt.Submit(Fixtures.Now.AddMinutes(5), result.Score, result.MaxScore);

        var review = await new AttemptReviewBuilder(_bank, _clock, _language).BuildAsync(attempt, _exam, CancellationToken.None);

        var shown = review.Sections.Single().Questions.Single();
        Assert.Equal("<p>फ्रांस की राजधानी?</p>", shown.Text);
        Assert.Equal(AnswerVerdict.Correct, shown.Verdict);
        var chosen = Assert.Single(shown.Options, o => o.WasChosen);
        Assert.Equal(("चार", true), (chosen.Text, chosen.IsCorrect));
    }
}
