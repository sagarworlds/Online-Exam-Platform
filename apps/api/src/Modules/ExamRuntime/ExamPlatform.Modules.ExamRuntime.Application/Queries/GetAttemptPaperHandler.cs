using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Shows staff the questions one attempt consisted of.</summary>
public sealed class GetAttemptPaperHandler(IExamCatalog catalog, IAttemptRepository attempts, IQuestionBank questionBank)
{
    /// <summary>Builds the paper of an attempt: the drawn one if the exam draws, otherwise the exam's fixed questions.</summary>
    /// <param name="examId">The exam the attempt belongs to.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is not an attempt at that exam.</exception>
    /// <exception cref="ExamContentUnavailableError">The attempt's exam can no longer be read.</exception>
    public async Task<AttemptPaperDto> HandleAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken);
        // The exam id in the route must match, so a staff link cannot be used to read an attempt of some other exam by accident.
        if (attempt is null || attempt.ExamId != examId)
            throw new AttemptNotFoundError();

        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamContentUnavailableError();
        var fixedIds = exam.Sections.SelectMany(s => s.QuestionIds).ToHashSet();
        var paper = exam.For(attempt);

        var ids = paper.Sections.SelectMany(s => s.QuestionIds).ToList();
        var texts = (await questionBank.GetAsync(ids, cancellationToken)).ToDictionary(q => q.Id, q => q.Text);

        var sections = paper.Sections
            .OrderBy(s => s.Order)
            .Select(s => new AttemptPaperSectionDto(
                s.Id,
                s.Name,
                s.QuestionIds.Select(id => new AttemptPaperQuestionDto(id, texts.GetValueOrDefault(id), !fixedIds.Contains(id))).ToList()))
            .ToList();

        return new AttemptPaperDto(attempt.Id, attempt.Number, sections.Any(s => s.Questions.Any(q => q.Drawn)), sections);
    }
}
