using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Creates a question (FR-5).</summary>
/// <param name="Text">The question text as the author's editor produced it (HTML); it is sanitized before it is stored.</param>
/// <param name="Options">The answer options in display order; exactly one correct.</param>
/// <param name="CreatedBy">The authoring user, taken from the caller's token.</param>
/// <param name="ChapterId">The chapter to file the question under, or null to leave it unfiled.</param>
/// <param name="Difficulty">"easy", "medium" or "hard", or null for unsaid.</param>
/// <param name="Topics">The question's topics, or null for none.</param>
/// <param name="AllowsMultiple">Whether more than one option may be correct.</param>
/// <param name="AllowDuplicate">Add the question even though the bank already holds one with the same wording and options (FR-9).</param>
public sealed record CreateQuestionCommand(
    string? Text, IReadOnlyList<NewQuestionOption>? Options, Guid CreatedBy, Guid? ChapterId = null,
    string? Difficulty = null, IReadOnlyList<string?>? Topics = null, bool AllowsMultiple = false, bool AllowDuplicate = false);

/// <summary>Handles <see cref="CreateQuestionCommand"/>.</summary>
public sealed class CreateQuestionHandler(
    IQuestionRepository repository,
    OpenChapterResolver chapters,
    QuestionDuplicateFinder duplicates,
    QuestionDuplicatePolicy duplicatePolicy,
    IQuestionBankUnitOfWork unitOfWork,
    IRichTextSanitizer sanitizer,
    Clock clock)
{
    /// <summary>Sanitizes the text, validates the question and stores it.</summary>
    /// <param name="command">The question to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored question.</returns>
    /// <exception cref="InvalidQuestionError">The question breaks one of the bank's rules.</exception>
    /// <exception cref="DuplicateQuestionError">The bank already has the same question and the author did not say to add it anyway.</exception>
    /// <exception cref="ChapterNotFoundError">The chapter to file it under does not exist.</exception>
    /// <exception cref="BookArchivedError">The chapter, or its book, is archived.</exception>
    public async Task<QuestionDto> HandleAsync(CreateQuestionCommand command, CancellationToken cancellationToken)
    {
        var cleaned = QuestionText.Clean(sanitizer, command.Text);

        var optionTexts = command.Options is null ? [] : command.Options.Select(o => o?.Text ?? string.Empty).ToList();
        if (duplicatePolicy.Refuse && !command.AllowDuplicate && (await duplicates.FindAsync(cleaned.PlainText, optionTexts, null, cancellationToken)).Any(m => m.SameOptions))
            throw new DuplicateQuestionError();

        var filedUnder = command.ChapterId is { } chapterId ? await chapters.ResolveAsync(chapterId, cancellationToken) : null;

        var question = Question.Create(
            cleaned.Html, command.Options, command.CreatedBy, clock.UtcNow, filedUnder?.ChapterId,
            QuestionDifficultyText.Parse(command.Difficulty), command.Topics, command.AllowsMultiple);
        question.IndexText(cleaned.PlainText);

        repository.Add(question);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return question.ToDto(filedUnder);
    }
}
