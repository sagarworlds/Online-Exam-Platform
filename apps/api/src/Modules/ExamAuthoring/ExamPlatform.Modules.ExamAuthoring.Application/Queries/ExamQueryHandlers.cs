using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Queries;

/// <summary>Lists the newest exams for the authoring screens.</summary>
public sealed class ListExamsHandler(IExamRepository repository, ExamDtoFactory dtos)
{
    /// <summary>How many exams one listing returns at most; paging arrives with the full authoring flow.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the newest exams, without their sections.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<ExamDto>> HandleAsync(CancellationToken cancellationToken) =>
        await dtos.ToDtosAsync(await repository.ListNewestAsync(PageSize, cancellationToken), cancellationToken);
}

/// <summary>Reads one exam with its sections and questions.</summary>
public sealed class GetExamHandler(IExamRepository repository, IQuestionBank questionBank, ExamDtoFactory dtos)
{
    /// <summary>Returns the exam, with each question's text read from the bank.</summary>
    /// <param name="examId">The exam's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    public async Task<ExamDto> HandleAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(examId, cancellationToken);

        var questionIds = exam.Sections.SelectMany(s => s.Questions).Select(q => q.QuestionVersionId).Distinct().ToList();
        var texts = (await questionBank.GetAsync(questionIds, cancellationToken)).ToDictionary(q => q.Id, q => q.Text);

        return await dtos.ToDetailDtoAsync(exam, texts, cancellationToken);
    }
}
