using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Builds the answer review of a submitted attempt (FR-32, FR-33). This is the only place the answer key is attached to
/// anything shown to a candidate, so it checks the two conditions itself rather than trusting its caller: the attempt must be
/// over, and the exam's author must have released the answers. A future caller cannot forget either check.
/// </summary>
public sealed class AttemptReviewBuilder(IQuestionBank questionBank, Clock clock)
{
    /// <summary>Builds the review.</summary>
    /// <param name="attempt">The candidate's own attempt.</param>
    /// <param name="exam">The exam it is an attempt at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotSubmittedError">The attempt is still open.</exception>
    /// <exception cref="ResultsNotReleasedError">The exam's author has not released the answers yet.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, or a saved answer names an option the question does not have.</exception>
    public async Task<AttemptReviewDto> BuildAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        if (attempt.Status != AttemptStatus.Submitted)
            throw new AttemptNotSubmittedError();

        var availability = ResultRelease.AvailabilityOf(exam, clock.UtcNow);
        if (!availability.Available)
            throw new ResultsNotReleasedError(availability.AvailableFromUtc);

        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = (await questionBank.GetAsync(questionIds, cancellationToken)).ToDictionary(q => q.Id);
        var chosen = attempt.Answers.ToDictionary(a => a.QuestionId, a => (IReadOnlyCollection<Guid>)a.SelectedOptionIds);

        var sections = exam.Sections
            .OrderBy(s => s.Order)
            .Select(section => new ReviewSectionDto(
                section.Id,
                section.Name,
                // In the order the candidate saw them, so "question 3" in the review is question 3 on the screen they sat.
                AttemptOrdering.Arrange(section.QuestionIds, id => id, attempt.Id, attempt.Number, section.Id, exam.ShuffleQuestions)
                    .Select(id => ReviewQuestion(attempt, exam, questions, id, chosen)).ToList()))
            .ToList();

        var marked = sections.SelectMany(s => s.Questions).ToList();
        return new AttemptReviewDto(
            attempt.Id,
            attempt.ExamId,
            exam.Name,
            attempt.Number,
            attempt.SubmittedAtUtc,
            attempt.AutoSubmitted,
            attempt.Score ?? throw new InvalidOperationException("A submitted attempt always has a score."),
            attempt.MaxScore ?? throw new InvalidOperationException("A submitted attempt always has a maximum score."),
            marked.Count(q => q.Verdict == AnswerVerdict.Correct),
            marked.Count(q => q.Verdict == AnswerVerdict.Wrong),
            marked.Count(q => q.Verdict == AnswerVerdict.Unanswered),
            sections,
            marked.Count(q => q.Verdict == AnswerVerdict.Partial));
    }

    private static ReviewQuestionDto ReviewQuestion(
        Attempt attempt,
        ExamSnapshot exam,
        IReadOnlyDictionary<Guid, QuestionSnapshot> questions,
        Guid questionId,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> chosen)
    {
        var question = questions.GetValueOrDefault(questionId) ?? throw new ExamContentUnavailableError();
        var chosenIds = chosen.TryGetValue(questionId, out var optionIds) ? optionIds : [];
        var mark = AttemptScorer.Mark(exam, question, chosenIds);

        return new ReviewQuestionDto(
            question.Id,
            question.Text,
            AttemptOrdering.Arrange(question.Options, o => o.Id, attempt.Id, attempt.Number, question.Id, exam.ShuffleOptions, o => o.IsPinned)
                .Select(o => new ReviewOptionDto(o.Id, o.Text, o.IsCorrect, chosenIds.Contains(o.Id))).ToList(),
            mark.Verdict,
            mark.Marks,
            question.AllowsMultiple);
    }
}
