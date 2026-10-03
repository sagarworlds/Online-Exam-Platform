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
            sections,
            // Only a finished attempt has anything to review, so an open one reports none.
            attempt.Status == AttemptStatus.Submitted ? ResultRelease.AvailabilityOf(exam, clock.UtcNow) : null,
            attempt.Number,
            exam.SectionLockEnabled,
            exam.SectionLockEnabled && attempt.Status == AttemptStatus.InProgress
                ? exam.Sections.FirstOrDefault(s => s.Order == attempt.ActiveSectionOrder)?.Id
                : null);
    }

    private async Task<IReadOnlyList<AttemptSectionDto>> BuildSectionsAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = (await questionBank.GetAsync(questionIds, cancellationToken)).ToDictionary(q => q.Id);
        var chosen = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.SelectedOptionIds);
        var marked = attempt.Marks.Select(m => m.QuestionId).ToHashSet();

        return exam.Sections
            .OrderBy(s => s.Order)
            .Select(section => new AttemptSectionDto(
                section.Id,
                section.Name,
                // Sections stay in the author's order; the questions in each, and the options of each question, are shuffled
                // from the second attempt on (AttemptOrdering), the same way every time this attempt is read.
                AttemptOrdering.Arrange(section.QuestionIds, id => id, attempt.Id, attempt.Number, section.Id, exam.ShuffleQuestions).Select(id =>
                {
                    var question = questions.GetValueOrDefault(id) ?? throw new ExamContentUnavailableError();
                    return new AttemptQuestionDto(
                        question.Id,
                        question.Text,
                        AttemptOrdering.Arrange(question.Options, o => o.Id, attempt.Id, attempt.Number, question.Id, exam.ShuffleOptions, o => o.IsPinned)
                            .Select(o => new AttemptOptionDto(o.Id, o.Text)).ToList(),
                        chosen.TryGetValue(id, out var optionIds) ? optionIds[0] : null,
                        marked.Contains(id),
                        question.AllowsMultiple,
                        chosen.TryGetValue(id, out var allIds) ? allIds : []);
                }).ToList()))
            .ToList();
    }
}
