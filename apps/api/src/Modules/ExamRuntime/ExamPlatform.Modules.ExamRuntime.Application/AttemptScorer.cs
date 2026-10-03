using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>The marks an attempt earned and the most it could have earned.</summary>
/// <param name="Score">The marks scored.</param>
/// <param name="MaxScore">The marks available.</param>
public readonly record struct AttemptScore(decimal Score, decimal MaxScore);

/// <summary>How one question was marked: the verdict and the marks it earned.</summary>
/// <param name="Verdict">Correct, wrong or unanswered.</param>
/// <param name="Marks">The marks earned, which the exam's marking scheme may make negative or zero.</param>
public readonly record struct QuestionMark(AnswerVerdict Verdict, decimal Marks);

/// <summary>Marks an attempt against the exam's marking scheme. Pure: everything it needs is passed in.</summary>
public static class AttemptScorer
{
    /// <summary>
    /// Marks one question. The one place the marking scheme is applied: <see cref="Score"/> adds these up and the answer
    /// review shows them, so a question's marks can never disagree with the total.
    /// </summary>
    /// <param name="exam">The exam, which holds the marking scheme.</param>
    /// <param name="question">The question from the question bank, answer key included.</param>
    /// <param name="chosenOptionId">The option the candidate chose, or null if they chose none.</param>
    /// <exception cref="ExamContentUnavailableError">The chosen option is not one of the question's options.</exception>
    public static QuestionMark Mark(ExamSnapshot exam, QuestionSnapshot question, Guid? chosenOptionId) =>
        Mark(exam, question, chosenOptionId is { } id ? new[] { id } : Array.Empty<Guid>());

    /// <summary>
    /// Marks one question against the set of options chosen. All or nothing: the answer is right only when the options chosen are
    /// exactly the question's correct options (for a single-answer question, the one correct option). Choosing some of them, or one
    /// too many, is wrong, because partly right is still not what the question asks for. Choosing nothing is unanswered.
    /// </summary>
    /// <param name="exam">The exam, which holds the marking scheme.</param>
    /// <param name="question">The question from the question bank, answer key included.</param>
    /// <param name="chosenOptionIds">The options the candidate chose; empty when they chose none.</param>
    /// <exception cref="ExamContentUnavailableError">A chosen option is not one of the question's options.</exception>
    public static QuestionMark Mark(ExamSnapshot exam, QuestionSnapshot question, IReadOnlyCollection<Guid> chosenOptionIds)
    {
        if (chosenOptionIds.Count == 0)
            return new QuestionMark(AnswerVerdict.Unanswered, exam.UnattemptedMarks);

        var chosen = chosenOptionIds.ToHashSet();
        var known = question.Options.Select(o => o.Id).ToHashSet();
        // A saved answer naming an option the question no longer has cannot be marked either way; failing loudly beats guessing.
        if (!chosen.IsSubsetOf(known))
            throw new ExamContentUnavailableError();

        var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        return chosen.SetEquals(correct)
            ? new QuestionMark(AnswerVerdict.Correct, exam.CorrectMarks)
            : new QuestionMark(AnswerVerdict.Wrong, exam.IncorrectMarks);
    }

    /// <summary>Scores every question of the exam: correct, wrong and unanswered each carry the exam's own marks.</summary>
    /// <param name="exam">The exam, with its marking scheme and the questions in it.</param>
    /// <param name="questions">The questions of the exam from the question bank, answer key included.</param>
    /// <param name="answers">The answers the candidate saved.</param>
    /// <returns>The marks scored and the marks available (the correct-answer marks of every question).</returns>
    /// <exception cref="ExamContentUnavailableError">A question of the exam is missing from <paramref name="questions"/>, or a saved answer names an option the question does not have.</exception>
    public static AttemptScore Score(
        ExamSnapshot exam,
        IReadOnlyDictionary<Guid, QuestionSnapshot> questions,
        IReadOnlyCollection<AttemptAnswer> answers)
    {
        var chosen = answers.ToDictionary(a => a.QuestionId, a => (IReadOnlyCollection<Guid>)a.SelectedOptionIds);
        decimal score = 0;
        decimal max = 0;

        foreach (var questionId in exam.Sections.SelectMany(s => s.QuestionIds))
        {
            // A question the bank no longer returns cannot be marked either way; failing loudly beats
            // quietly awarding or withholding marks for it.
            if (!questions.TryGetValue(questionId, out var question))
                throw new ExamContentUnavailableError();

            max += exam.CorrectMarks;
            score += Mark(exam, question, chosen.TryGetValue(questionId, out var optionIds) ? optionIds : []).Marks;
        }

        return new AttemptScore(score, max);
    }
}
