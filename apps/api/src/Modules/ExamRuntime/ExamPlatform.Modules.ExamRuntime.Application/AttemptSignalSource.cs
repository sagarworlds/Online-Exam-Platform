using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Reads the facts of finished attempts for the risk review (FR-27), marking each saved answer with the same <see cref="AttemptScorer"/>
/// the score and the answer review use, so a "wrong" here is the same "wrong" a candidate sees in their review.
/// </summary>
/// <remarks>
/// Only the answers are marked. An unanswered question cannot be a shared wrong answer, so the paper is not read, and the question bank
/// is asked for the questions an attempt answered, one read per attempt, in the version that attempt sat.
/// </remarks>
public sealed class AttemptSignalSource(IAttemptRepository attempts, IExamCatalog catalog, IQuestionBank questionBank) : IAttemptSignalSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<FinishedAttemptRef>> ListFinishedAttemptRefsAsync(Guid examId, CancellationToken cancellationToken)
    {
        // An unknown exam has no attempts; the caller decides whether that is a 404.
        if (await catalog.FindAsync(examId, cancellationToken) is null)
        {
            return [];
        }

        var finished = await attempts.ListFinishedForExamAsync(examId, cancellationToken);
        return finished
            .Select(a => new FinishedAttemptRef(
                a.Id,
                a.CandidateId,
                a.StartedAtUtc))
            .ToList();
    }

    /// <inheritdoc />
    /// <exception cref="ExamContentUnavailableError">An attempt holds a saved answer naming an option its question no longer has.</exception>
    public async Task<IReadOnlyList<AttemptSignals>> ListFinishedAttemptsAsync(Guid examId, IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken)
    {
        if (attemptIds.Count == 0)
        {
            return [];
        }

        var exam = await catalog.FindAsync(examId, cancellationToken);
        if (exam is null)
        {
            return [];
        }

        var finished = await attempts.ListFinishedWithAnswersAsync(examId, attemptIds, cancellationToken);
        var signals = new List<AttemptSignals>(finished.Count);
        foreach (var attempt in finished)
        {
            signals.Add(await SignalsOfAsync(attempt, exam, cancellationToken));
        }

        return signals;
    }

    private async Task<AttemptSignals> SignalsOfAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        var answeredIds = attempt.Answers.Select(a => a.QuestionId).Distinct().ToList();
        var questions = answeredIds.Count == 0
            ? new Dictionary<Guid, QuestionSnapshot>()
            : await questionBank.ReadForCandidateAsync(attempt, answeredIds, [], cancellationToken);

        var wrong = new List<WrongAnswer>();
        foreach (var answer in attempt.Answers)
        {
            var question = questions.GetValueOrDefault(answer.QuestionId) ?? throw new ExamContentUnavailableError();
            // Partial credit is not wrong: a candidate who got part of a multiple-answer question right is not sharing a mistake.
            var mark = AttemptScorer.Mark(exam, question, answer.SelectedOptionIds, answer.AnswerText);
            if (mark.Verdict == AnswerVerdict.Wrong)
            {
                wrong.Add(new WrongAnswer(answer.QuestionId, ChoiceKeyOf(answer)));
            }
        }

        return new AttemptSignals(
            AttemptId: attempt.Id,
            ExamId: attempt.ExamId,
            CandidateId: attempt.CandidateId,
            Number: attempt.Number,
            StartedAtUtc: attempt.StartedAtUtc,
            SubmittedAtUtc: attempt.SubmittedAtUtc ?? throw new InvalidOperationException("A submitted attempt always has a submission time."),
            IsInvalidated: attempt.IsInvalidated,
            AnsweredCount: attempt.Answers.Count,
            FocusDepartures: attempt.FocusViolations.Count,
            ClientChanges: attempt.ClientSightings.Count(s => s.Reason == ClientSightingReason.Changed),
            WrongAnswers: wrong);
    }

    /// <summary>
    /// The choice as a comparable key. Options are sorted so the same set chosen in another order is the same answer; typed text is
    /// compared the way the candidate's own answer is marked (spaces collapsed, case ignored), so "Paris" and " paris " match.
    /// </summary>
    private static string ChoiceKeyOf(AttemptAnswer answer)
    {
        if (answer.AnswerText is { } typed)
        {
            return string.Join(' ', typed.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        return string.Join(',', answer.SelectedOptionIds.OrderBy(id => id).Select(id => id.ToString("N")));
    }
}
