using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Edits a question (FR-5, FR-7).</summary>
/// <param name="QuestionId">The question to edit.</param>
/// <param name="Text">The new question text as the author's editor produced it (HTML); it is sanitized before it is stored.</param>
/// <param name="Options">All the options after the edit, in display order; options that already exist are named by id.</param>
/// <param name="Difficulty">"easy", "medium" or "hard", or null for unsaid. Like the rest it replaces what was there.</param>
/// <param name="Topics">The question's topics after the edit, or null for none.</param>
public sealed record EditQuestionCommand(
    Guid QuestionId, string? Text, IReadOnlyList<QuestionOptionEdit>? Options,
    string? Difficulty = null, IReadOnlyList<string?>? Topics = null);

/// <summary>Handles <see cref="EditQuestionCommand"/>.</summary>
public sealed class EditQuestionHandler(
    IQuestionRepository repository,
    IQuestionBankUnitOfWork unitOfWork,
    IRichTextSanitizer sanitizer,
    QuestionUsageReader usageReader,
    QuestionDtoFactory dtos)
{
    /// <summary>Sanitizes the text, checks the question against its rules and the lock for answered questions, and stores it.</summary>
    /// <param name="command">The edit to make.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The question as stored.</returns>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionError">The edited question breaks one of the bank's rules.</exception>
    /// <exception cref="QuestionLockedError">Candidates have answered the question and the edit changes more than its wording.</exception>
    public async Task<QuestionDto> HandleAsync(EditQuestionCommand command, CancellationToken cancellationToken)
    {
        var cleaned = QuestionText.Clean(sanitizer, command.Text);

        var question = await repository.GetByIdAsync(command.QuestionId, cancellationToken) ?? throw new QuestionNotFoundError();

        // Asked after loading and just before changing, so the lock is decided on the freshest answer there is.
        var usage = await usageReader.ReadOneAsync(question.Id, cancellationToken);
        question.Revise(cleaned.Html, command.Options, usage.Answered);
        question.IndexText(cleaned.PlainText);
        // Labels never reach a candidate, so they are not covered by the lock Revise applies to an answered question.
        question.Classify(QuestionDifficultyText.Parse(command.Difficulty), command.Topics);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.CreateAsync(question, usage, cancellationToken);
    }
}
