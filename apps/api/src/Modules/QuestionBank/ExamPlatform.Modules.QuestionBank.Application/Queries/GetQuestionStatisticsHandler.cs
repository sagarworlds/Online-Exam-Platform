using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Queries;

/// <summary>Reads where a question is used and how candidates have answered it (FR-9).</summary>
public sealed class GetQuestionStatisticsHandler(IQuestionRepository repository, QuestionUsageReader usageReader, IQuestionStatisticsSource statistics)
{
    /// <summary>Returns the exams that hold the question and how its answers have gone.</summary>
    /// <param name="questionId">The question's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    public async Task<QuestionStatisticsDto> HandleAsync(Guid questionId, CancellationToken cancellationToken)
    {
        var question = await repository.GetByIdAsync(questionId, cancellationToken) ?? throw new QuestionNotFoundError();
        var usage = await usageReader.ReadOneAsync(questionId, cancellationToken);
        var answers = await statistics.ReadAsync(questionId, cancellationToken);

        return new QuestionStatisticsDto(
            usage.ExamCount,
            usage.ExamNames,
            answers.Answered,
            answers.Correct,
            answers.Answered == 0 ? null : Math.Round(100m * answers.Correct / answers.Answered, 1),
            question.Options.OrderBy(o => o.Order)
                .Select(o => new OptionStatisticsDto(o.Id, o.Text, o.IsCorrect, answers.Chosen.GetValueOrDefault(o.Id)))
                .ToList());
    }
}
