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
public sealed record CreateQuestionCommand(
    string? Text, IReadOnlyList<NewQuestionOption>? Options, Guid CreatedBy, Guid? ChapterId = null);

/// <summary>Handles <see cref="CreateQuestionCommand"/>.</summary>
public sealed class CreateQuestionHandler(
    IQuestionRepository repository,
    OpenChapterResolver chapters,
    IQuestionBankUnitOfWork unitOfWork,
    IRichTextSanitizer sanitizer,
    Clock clock)
{
    /// <summary>Sanitizes the text, validates the question and stores it.</summary>
    /// <param name="command">The question to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored question.</returns>
    /// <exception cref="InvalidQuestionError">The question breaks one of the bank's rules.</exception>
    /// <exception cref="ChapterNotFoundError">The chapter to file it under does not exist.</exception>
    /// <exception cref="BookArchivedError">The chapter, or its book, is archived.</exception>
    public async Task<QuestionDto> HandleAsync(CreateQuestionCommand command, CancellationToken cancellationToken)
    {
        var html = QuestionText.Clean(sanitizer, command.Text);

        var filedUnder = command.ChapterId is { } chapterId ? await chapters.ResolveAsync(chapterId, cancellationToken) : null;

        var question = Question.Create(html, command.Options, command.CreatedBy, clock.UtcNow, filedUnder?.ChapterId);

        repository.Add(question);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return question.ToDto(filedUnder);
    }
}
