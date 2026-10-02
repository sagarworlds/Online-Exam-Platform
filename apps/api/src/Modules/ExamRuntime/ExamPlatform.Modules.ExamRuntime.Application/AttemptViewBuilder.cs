using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Builds what a candidate is shown for an attempt. This is the one place a question leaves the module on its
/// way to a candidate, so it is also the one place the answer key is dropped.
/// </summary>
public sealed class AttemptViewBuilder(IQuestionBank questionBank, Clock clock)
{
    /// <summary>Builds the candidate's view of an attempt.</summary>
    /// <param name="attempt">The attempt.</param>
    /// <param name="exam">The exam it is an attempt at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt with its questions while it is open, or with its score once it is submitted.</returns>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read.</exception>
    public async Task<AttemptDto> BuildAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        var sections = attempt.Status == AttemptStatus.InProgress
            ? await BuildSectionsAsync(attempt, exam, cancellationToken)
            : [];

        return new AttemptDto(
            attempt.Id,
            attempt.ExamId,
            exam.Name,
            attempt.Status,
            attempt.StartedAtUtc,
            attempt.DeadlineUtc,
            attempt.SubmittedAtUtc,
            attempt.AutoSubmitted,
            attempt.Score,
            attempt.MaxScore,
            clock.UtcNow,
            sections);
    }

    private async Task<IReadOnlyList<AttemptSectionDto>> BuildSectionsAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = (await questionBank.GetAsync(questionIds, cancellationToken)).ToDictionary(q => q.Id);
        var chosen = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.SelectedOptionId);

        return exam.Sections
            .OrderBy(s => s.Order)
            .Select(section => new AttemptSectionDto(
                section.Id,
                section.Name,
                section.QuestionIds.Select(id =>
                {
                    var question = questions.GetValueOrDefault(id) ?? throw new ExamContentUnavailableError();
                    return new AttemptQuestionDto(
                        question.Id,
                        question.Text,
                        question.Options.Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList(),
                        chosen.TryGetValue(id, out var optionId) ? optionId : null);
                }).ToList()))
            .ToList();
    }
}
