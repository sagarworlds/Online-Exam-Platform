using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Lists the newest questions for the authoring screens.</summary>
public sealed class ListQuestionsHandler(IQuestionRepository repository, IBookRepository books, QuestionUsageReader usageReader)
{
    /// <summary>How many questions one listing returns at most; later pages are reached with <c>skip</c>.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the newest questions that match the filter, answer key and usage included.</summary>
    /// <param name="filter">Which questions to include; none set means all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="skip">How many of the newest matching questions to leave out; 0 (or a negative number) starts at the newest.</param>
    public async Task<IReadOnlyList<QuestionDto>> HandleAsync(QuestionFilter filter, CancellationToken cancellationToken, int skip = 0)
    {
        var found = await repository.ListNewestAsync(filter, Math.Max(0, skip), PageSize, cancellationToken);
        var chapters = await books.GetChapterRefsAsync(
            found.Where(q => q.ChapterId is not null).Select(q => q.ChapterId!.Value).Distinct().ToList(), cancellationToken);
        var usage = await usageReader.ReadAsync(found.Select(q => q.Id).ToList(), cancellationToken);

        return found.Select(q => q.ToDto(q.ChapterId is { } id ? chapters.GetValueOrDefault(id) : null, usage[q.Id])).ToList();
    }
}

/// <summary>Lists the topics in use, for the authoring screens to offer as a filter and as suggestions.</summary>
public sealed class ListTopicsHandler(IQuestionRepository repository)
{
    /// <summary>Returns every topic any question carries, once each, alphabetically.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<string>> HandleAsync(CancellationToken cancellationToken) =>
        await repository.ListTopicsAsync(cancellationToken);
}

/// <summary>Reads one question for the authoring screens.</summary>
public sealed class GetQuestionHandler(IQuestionRepository repository, QuestionUsageReader usageReader, QuestionDtoFactory dtos)
{
    /// <summary>Returns the question with its answer key and where it is in use.</summary>
    /// <param name="questionId">The question's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    public async Task<QuestionDto> HandleAsync(Guid questionId, CancellationToken cancellationToken)
    {
        var question = await repository.GetByIdAsync(questionId, cancellationToken) ?? throw new QuestionNotFoundError();

        return await dtos.CreateAsync(question, await usageReader.ReadOneAsync(questionId, cancellationToken), cancellationToken);
    }
}

/// <summary>Reads a question's review thread (FR-8).</summary>
public sealed class GetQuestionReviewLogHandler(IQuestionRepository repository)
{
    /// <summary>Returns every comment and workflow step of the question, oldest first.</summary>
    /// <param name="questionId">The question's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    public async Task<IReadOnlyList<QuestionReviewEntryDto>> HandleAsync(Guid questionId, CancellationToken cancellationToken)
    {
        // Not loaded with its versions: only whether it exists matters here.
        _ = (await repository.GetManyAsync([questionId], cancellationToken)).FirstOrDefault() ?? throw new QuestionNotFoundError();

        return (await repository.ListReviewEntriesAsync(questionId, cancellationToken)).Select(e => e.ToDto()).ToList();
    }
}

/// <summary>Reads a question's version history (FR-7).</summary>
public sealed class GetQuestionHistoryHandler(IQuestionRepository repository)
{
    /// <summary>Returns every version the question has had, oldest first.</summary>
    /// <param name="questionId">The question's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    public async Task<IReadOnlyList<QuestionVersionDto>> HandleAsync(Guid questionId, CancellationToken cancellationToken)
    {
        var question = await repository.GetByIdAsync(questionId, cancellationToken) ?? throw new QuestionNotFoundError();

        return question.Versions.OrderBy(v => v.VersionNumber).Select(v => v.ToDto()).ToList();
    }
}
