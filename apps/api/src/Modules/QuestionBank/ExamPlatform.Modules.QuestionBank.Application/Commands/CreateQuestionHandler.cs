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
    IBookRepository books,
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

        var filedUnder = command.ChapterId is { } chapterId ? await FindOpenChapterAsync(chapterId, cancellationToken) : null;

        var question = Question.Create(text.Html, command.Options, command.CreatedBy, clock.UtcNow, filedUnder?.ChapterId);

        repository.Add(question);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return question.ToDto(filedUnder);
    }

    // A question may only be filed under a chapter that exists and is still open: an archived chapter keeps what it
    // has but takes nothing new, which is what archiving is for.
    private async Task<ChapterRef> FindOpenChapterAsync(Guid chapterId, CancellationToken cancellationToken)
    {
        var book = await books.GetByChapterIdAsync(chapterId, cancellationToken) ?? throw new ChapterNotFoundError();
        var chapter = book.GetChapter(chapterId);

        if (book.IsArchived)
            throw new BookArchivedError($"The book \"{book.Name}\" is archived; restore it or choose another chapter.");
        if (chapter.IsArchived)
            throw new BookArchivedError($"The chapter \"{chapter.Title}\" is archived; restore it or choose another chapter.");

        return new ChapterRef(chapter.Id, chapter.Title, book.Id, book.Name);
    }
}
