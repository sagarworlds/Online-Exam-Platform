using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Files questions under a chapter (FR-5).</summary>
/// <param name="QuestionIds">The questions to file; one is a single question, several is a bulk move.</param>
/// <param name="ChapterId">The chapter to file them under.</param>
public sealed record FileQuestionsCommand(IReadOnlyList<Guid>? QuestionIds, Guid ChapterId);

/// <summary>What filing questions did.</summary>
/// <param name="Moved">How many questions changed place; one already in the chapter is not counted.</param>
/// <param name="ChapterId">The chapter they were filed under.</param>
/// <param name="ChapterTitle">That chapter's title.</param>
/// <param name="BookId">The book the chapter belongs to.</param>
/// <param name="BookName">That book's name.</param>
public sealed record FileQuestionsResult(int Moved, Guid ChapterId, string ChapterTitle, Guid BookId, string BookName);

/// <summary>Handles <see cref="FileQuestionsCommand"/>.</summary>
public sealed class FileQuestionsHandler(
    IQuestionRepository repository,
    OpenChapterResolver chapters,
    IEnumerable<IQuestionPlacementGuard> guards,
    IQuestionBankUnitOfWork unitOfWork)
{
    /// <summary>The most questions one request may file: a whole page of the listing.</summary>
    public const int MaxQuestions = ListQuestionsHandler.PageSize;

    /// <summary>Files the questions under the chapter, all of them or none.</summary>
    /// <remarks>
    /// One rule for one question and for many, so a bulk move cannot do what a single move would refuse. Nothing is saved
    /// unless every question may move: a half-filed selection is harder for an author to fix than a refused one.
    /// </remarks>
    /// <param name="command">The questions and the chapter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidQuestionError">No question, or more than <see cref="MaxQuestions"/>, was chosen.</exception>
    /// <exception cref="ChapterNotFoundError">The chapter does not exist.</exception>
    /// <exception cref="BookArchivedError">The chapter, or its book, is archived.</exception>
    /// <exception cref="QuestionNotFoundError">One of the questions does not exist.</exception>
    /// <exception cref="PlacementRefusedError">Something that depends on a question's place, such as a draft exam limited to other chapters, objects.</exception>
    public async Task<FileQuestionsResult> HandleAsync(FileQuestionsCommand command, CancellationToken cancellationToken)
    {
        var ids = command.QuestionIds?.Distinct().ToList() ?? [];
        if (ids.Count is 0 or > MaxQuestions)
            throw new InvalidQuestionError($"Choose between 1 and {MaxQuestions} questions to file.");

        var chapter = await chapters.ResolveAsync(command.ChapterId, cancellationToken);

        var questions = await repository.GetManyForUpdateAsync(ids, cancellationToken);
        if (questions.Count != ids.Count)
            throw new QuestionNotFoundError();

        await EnsureNothingObjectsAsync(ids, chapter, cancellationToken);

        var moved = questions.Count(question => question.FileUnder(chapter.ChapterId));
        if (moved > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return new FileQuestionsResult(moved, chapter.ChapterId, chapter.ChapterTitle, chapter.BookId, chapter.BookName);
    }

    private async Task EnsureNothingObjectsAsync(IReadOnlyCollection<Guid> ids, ChapterRef chapter, CancellationToken cancellationToken)
    {
        var objections = new List<PlacementObjection>();
        foreach (var guard in guards)
            objections.AddRange(await guard.CheckAsync(ids, chapter.BookId, chapter.ChapterId, cancellationToken));

        if (objections.Count == 0)
            return;

        var others = objections.Select(o => o.QuestionId).Distinct().Count() - 1;
        var more = others > 0 ? $" {others} other {(others == 1 ? "question is" : "questions are")} held back for the same reason." : string.Empty;
        throw new PlacementRefusedError(objections[0].Reason + more + " Nothing was moved.");
    }
}
