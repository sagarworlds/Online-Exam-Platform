using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Adds a translation of a question (FR-10).</summary>
/// <param name="SourceQuestionId">The question being translated; any question of its group will do.</param>
/// <param name="Language">The language of the translation, such as "hi".</param>
/// <param name="Text">The translated question text as the author's editor produced it (HTML); it is sanitized before it is stored.</param>
/// <param name="OptionTexts">The translated options, one for each option of the source, in the same order.</param>
/// <param name="CreatedBy">The authoring user, taken from the caller's token.</param>
public sealed record AddTranslationCommand(
    Guid SourceQuestionId, string? Language, string? Text, IReadOnlyList<string?>? OptionTexts, Guid CreatedBy);

/// <summary>Handles <see cref="AddTranslationCommand"/>.</summary>
public sealed class AddTranslationHandler(
    IQuestionRepository repository,
    QuestionDtoFactory dtos,
    IQuestionBankUnitOfWork unitOfWork,
    IRichTextSanitizer sanitizer,
    Clock clock)
{
    /// <summary>
    /// Stores a new question, in another language, that says what the source says. It takes the source's answer key, chapter, difficulty,
    /// topics and whether it takes several answers, so the two can never disagree about what is right; the translator supplies only words.
    /// </summary>
    /// <remarks>
    /// The translation is a draft and a question in its own right: it is reviewed, versioned and answered separately from the source,
    /// and editing either leaves the other alone. What links them is the shared <see cref="Question.TranslationGroupId"/>.
    /// </remarks>
    /// <param name="command">The translation to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new question.</returns>
    /// <exception cref="QuestionNotFoundError">No question has the source's id.</exception>
    /// <exception cref="InvalidQuestionError">The language is missing or unsupported, the text is unusable, or the options are not one per option of the source.</exception>
    /// <exception cref="TranslationExistsError">The group already has a question in that language, the source's own included.</exception>
    public async Task<QuestionDto> HandleAsync(AddTranslationCommand command, CancellationToken cancellationToken)
    {
        var source = await repository.GetByIdAsync(command.SourceQuestionId, cancellationToken) ?? throw new QuestionNotFoundError();

        // Unlike creating a question, there is no default: a translation that silently came out English would be no translation.
        if (string.IsNullOrWhiteSpace(command.Language))
            throw new InvalidQuestionError("Say which language the translation is in.");
        var language = QuestionLanguage.Parse(command.Language);

        if ((await repository.ListTranslationGroupAsync(source.TranslationGroupId, cancellationToken)).Any(q => q.Language == language))
            throw new TranslationExistsError(language);

        var cleaned = QuestionText.Clean(sanitizer, command.Text);

        var sourceOptions = source.Options.OrderBy(o => o.Order).ToList();
        if (command.OptionTexts is null || command.OptionTexts.Count != sourceOptions.Count)
            throw new InvalidQuestionError($"A translation needs {sourceOptions.Count} options, one for each option of the question it translates, in the same order.");

        var options = sourceOptions.Select((o, i) => new NewQuestionOption(command.OptionTexts[i], o.IsCorrect, o.IsPinned)).ToList();
        var translation = Question.Create(
            cleaned.Html, options, command.CreatedBy, clock.UtcNow, source.ChapterId, source.Difficulty, source.Topics,
            source.AllowsMultiple, language, source.TranslationGroupId);
        translation.IndexText(cleaned.PlainText);

        repository.Add(translation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.CreateAsync(translation, QuestionUsageDto.Unused, cancellationToken);
    }
}
