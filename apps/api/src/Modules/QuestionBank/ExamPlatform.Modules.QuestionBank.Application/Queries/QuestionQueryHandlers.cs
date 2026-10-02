using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Lists the newest questions for the authoring screens.</summary>
public sealed class ListQuestionsHandler(IQuestionRepository repository)
{
    /// <summary>How many questions one listing returns at most; paging arrives with the full bank.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the newest questions, answer key included.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<QuestionDto>> HandleAsync(CancellationToken cancellationToken) =>
        (await repository.ListNewestAsync(PageSize, cancellationToken)).Select(q => q.ToDto()).ToList();
}

/// <summary>Reads one question for the authoring screens.</summary>
public sealed class GetQuestionHandler(IQuestionRepository repository)
{
    /// <summary>Returns the question with its answer key.</summary>
    /// <param name="questionId">The question's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    public async Task<QuestionDto> HandleAsync(Guid questionId, CancellationToken cancellationToken) =>
        (await repository.GetByIdAsync(questionId, cancellationToken) ?? throw new QuestionNotFoundError()).ToDto();
}
