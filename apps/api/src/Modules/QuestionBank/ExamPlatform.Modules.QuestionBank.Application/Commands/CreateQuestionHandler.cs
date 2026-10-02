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
public sealed record CreateQuestionCommand(string? Text, IReadOnlyList<NewQuestionOption>? Options, Guid CreatedBy);

/// <summary>Handles <see cref="CreateQuestionCommand"/>.</summary>
public sealed class CreateQuestionHandler(
    IQuestionRepository repository,
    IQuestionBankUnitOfWork unitOfWork,
    IRichTextSanitizer sanitizer,
    Clock clock)
{
    /// <summary>Sanitizes the text, validates the question and stores it.</summary>
    /// <param name="command">The question to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored question.</returns>
    /// <exception cref="InvalidQuestionError">The question breaks one of the bank's rules.</exception>
    public async Task<QuestionDto> HandleAsync(CreateQuestionCommand command, CancellationToken cancellationToken)
    {
        // Sanitizing comes first: every length rule below is about what survives the cleaning, not what was sent.
        var text = sanitizer.Sanitize(command.Text);

        if (text.RejectedImageCount > 0)
            throw new InvalidQuestionError(
                "A picture could not be used. Add pictures with the image button: PNG, JPEG, GIF or WebP, " +
                $"at most {Question.MaxImageBytes / 1024} KB each.");
        if (!text.HasContent)
            throw new InvalidQuestionError("The question text is required.");
        if (text.PlainText.Length > Question.MaxVisibleTextLength)
            throw new InvalidQuestionError($"The question text must be at most {Question.MaxVisibleTextLength} characters.");
        if (text.ImageCount > Question.MaxImages)
            throw new InvalidQuestionError($"A question can have at most {Question.MaxImages} images.");

        var question = Question.Create(text.Html, command.Options, command.CreatedBy, clock.UtcNow);

        repository.Add(question);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return question.ToDto();
    }
}
