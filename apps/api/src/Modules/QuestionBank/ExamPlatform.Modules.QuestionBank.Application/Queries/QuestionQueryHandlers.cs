using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Lists the newest questions for the authoring screens.</summary>
public sealed class ListQuestionsHandler(IQuestionRepository repository, IBookRepository books, QuestionUsageReader usageReader)
{
    /// <summary>How many questions one listing returns at most; paging arrives with the full bank.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the newest questions that match the filter, answer key and usage included.</summary>
    /// <param name="filter">Which questions to include; none set means all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<QuestionDto>> HandleAsync(QuestionFilter filter, CancellationToken cancellationToken)
    {
        var found = await repository.ListNewestAsync(filter, PageSize, cancellationToken);
        var chapters = await books.GetChapterRefsAsync(
            found.Where(q => q.ChapterId is not null).Select(q => q.ChapterId!.Value).Distinct().ToList(), cancellationToken);
        var usage = await usageReader.ReadAsync(found.Select(q => q.Id).ToList(), cancellationToken);

        return found.Select(q => q.ToDto(q.ChapterId is { } id ? chapters.GetValueOrDefault(id) : null, usage[q.Id])).ToList();
    }
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
