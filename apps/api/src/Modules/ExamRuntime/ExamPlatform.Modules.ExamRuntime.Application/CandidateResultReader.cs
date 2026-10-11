using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Serves a candidate's released results to other modules (<see cref="ICandidateResultReader"/>). It decides what counts as a released result,
/// the one rule for "can the candidate see this", so Analytics can never show a result the candidate could not open themselves.
/// </summary>
/// <remarks>
/// The marks are worked out with <see cref="AttemptScorer"/> and the version-aware question read, so a section total here agrees with the
/// answer review and the result page for the same attempt. The review builder is not reused because it also loads the option texts and
/// the translations, which a summary of marks never needs.
/// </remarks>
public sealed class CandidateResultReader(IAttemptRepository attempts, IExamCatalog catalog, IQuestionBank questionBank, Clock clock) : ICandidateResultReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CandidateResult>> ListReleasedAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        var counted = await attempts.ListCountedForCandidateAsync(candidateId, cancellationToken);
        var now = clock.UtcNow;
        var examsById = new Dictionary<Guid, ExamSnapshot>();
        var results = new List<CandidateResult>();

        foreach (var attempt in counted)
        {
            var exam = await ExamOfAsync(attempt.ExamId, examsById, cancellationToken);

            // A held result is left out, not reported as zero: until the author releases it, how the candidate did is not known to them.
            // The same rule decides the answer review (FR-12, FR-33), so the two can never disagree.
            if (!ResultRelease.IsReleased(exam, now))
                continue;

            results.Add(await BuildAsync(attempt, exam.For(attempt), cancellationToken));
        }

        return results;
    }

    private async Task<ExamSnapshot> ExamOfAsync(Guid examId, Dictionary<Guid, ExamSnapshot> examsById, CancellationToken cancellationToken)
    {
        if (examsById.TryGetValue(examId, out var known))
            return known;

        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamContentUnavailableError();
        examsById[examId] = exam;
        return exam;
    }

    private async Task<CandidateResult> BuildAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).Distinct().ToList();
        var questions = await questionBank.ReadAsync(attempt, questionIds, cancellationToken);
        var answers = attempt.Answers.ToDictionary(a => a.QuestionId);

        var sections = exam.Sections
            .OrderBy(s => s.Order)
            .Select(section => SectionOf(section, exam, questions, answers))
            .ToList();

        // A counted attempt is always submitted and scored; the checks only satisfy the nullable columns' types.
        return new CandidateResult(
            attempt.Id,
            attempt.ExamId,
            exam.Name,
            attempt.SubmittedAtUtc ?? throw new InvalidOperationException("A submitted attempt always has a submission time."),
            attempt.Score ?? throw new InvalidOperationException("A submitted attempt always has a score."),
            attempt.MaxScore ?? throw new InvalidOperationException("A submitted attempt always has a maximum score."),
            sections);
    }

    private static CandidateSectionResult SectionOf(
        ExamSectionSnapshot section,
        ExamSnapshot exam,
        IReadOnlyDictionary<Guid, QuestionSnapshot> questions,
        IReadOnlyDictionary<Guid, AttemptAnswer> answers)
    {
        var marks = section.QuestionIds.Select(id => MarkOf(exam, questions, answers, id)).ToList();

        return new CandidateSectionResult(
            section.Id,
            section.Name,
            marks.Sum(m => m.Marks),
            marks.Count(m => m.Verdict == AnswerVerdict.Correct),
            marks.Count(m => m.Verdict == AnswerVerdict.Wrong),
            marks.Count(m => m.Verdict == AnswerVerdict.Partial),
            marks.Count(m => m.Verdict == AnswerVerdict.Unanswered));
    }

    private static QuestionMark MarkOf(
        ExamSnapshot exam,
        IReadOnlyDictionary<Guid, QuestionSnapshot> questions,
        IReadOnlyDictionary<Guid, AttemptAnswer> answers,
        Guid questionId)
    {
        // A question the bank cannot return cannot be marked either way, so the result is refused rather than scored as a guess.
        var question = questions.GetValueOrDefault(questionId) ?? throw new ExamContentUnavailableError();
        var answer = answers.GetValueOrDefault(questionId);
        return AttemptScorer.Mark(exam, question, answer?.SelectedOptionIds ?? [], answer?.AnswerText);
    }
}
