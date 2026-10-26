using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Grading;

/// <summary>Which of an attempt's typed answers the AI evaluator is to be asked about. The one place that rule lives.</summary>
public static class TypedAnswerChecks
{
    /// <summary>
    /// Where the AI evaluator stands on one typed answer. Shown on the paper and used to pick the answers a bulk recheck sends, so what staff
    /// read on the paper ("Not checked", "Not judged by the AI") is exactly what the recheck acts on.
    /// </summary>
    /// <param name="answer">The answer.</param>
    /// <returns>Waiting while it is queued; otherwise given up on, judged, or never asked about.</returns>
    public static AiCheckState StateOf(AttemptAnswer answer) =>
        answer.IsAwaitingEvaluation ? AiCheckState.Waiting
        : answer.EvaluationGaveUpAtUtc is not null ? AiCheckState.GaveUp
        : answer.TextVerdict is not null ? AiCheckState.Judged
        : AiCheckState.NotChecked;

    /// <summary>
    /// The questions whose typed answers need a verdict and have none: never asked about, or given up on. An answer already judged, or
    /// waiting in the queue, is left out, so asking again never spends a call on something that has an answer or is about to get one.
    /// </summary>
    /// <param name="attempt">The attempt, with its answers.</param>
    /// <param name="questions">The questions of the attempt, answer key included, as the attempt sat them.</param>
    /// <returns>The question ids; empty when every answer the evaluator would judge already has a verdict or is waiting for one.</returns>
    public static IReadOnlyList<Guid> QuestionsWithoutVerdict(Attempt attempt, IReadOnlyDictionary<Guid, QuestionSnapshot> questions)
    {
        var toCheck = QuestionsToCheck(attempt, questions).ToHashSet();
        return attempt.Answers
            .Where(a => toCheck.Contains(a.QuestionId) && StateOf(a) is AiCheckState.NotChecked or AiCheckState.GaveUp)
            .Select(a => a.QuestionId)
            .ToList();
    }

    /// <summary>
    /// The questions whose typed answers need a verdict: the candidate typed something, and it does not match one of the accepted answers
    /// under the question's matching rule. An answer that does is right without being asked, and a blank one is unanswered, so neither costs a call.
    /// </summary>
    /// <param name="attempt">The attempt, with its answers.</param>
    /// <param name="questions">The questions of the attempt, answer key included, as the attempt sat them.</param>
    /// <returns>The question ids; empty when the evaluator has nothing to judge.</returns>
    public static IReadOnlyList<Guid> QuestionsToCheck(Attempt attempt, IReadOnlyDictionary<Guid, QuestionSnapshot> questions)
    {
        var found = new List<Guid>();
        foreach (var answer in attempt.Answers)
        {
            if (string.IsNullOrWhiteSpace(answer.AnswerText))
                continue;

            // A question the exam no longer has, or one answered by choosing options, is not the evaluator's to judge.
            if (!questions.TryGetValue(answer.QuestionId, out var question) || !question.IsTextAnswer)
                continue;

            var accepted = question.AcceptedAnswers ?? [];
            if (accepted.Count == 0 || TypedAnswer.Matches(answer.AnswerText, accepted, question.MatchRule))
                continue;

            found.Add(answer.QuestionId);
        }

        return found;
    }
}
