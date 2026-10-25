using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Grading;

/// <summary>Which of an attempt's typed answers the AI evaluator is to be asked about. The one place that rule lives.</summary>
public static class TypedAnswerChecks
{
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
