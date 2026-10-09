using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;

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
    /// Marks one question against the set of options chosen. The answer is right when the options chosen are exactly the question's
    /// correct options (for a single-answer question, the one correct option). Choosing nothing is unanswered.
    /// </summary>
    /// <remarks>
    /// Anything else is wrong, unless the exam gives partial credit: then a multiple-answer question earns
    /// <c>(correct options chosen - wrong options chosen) / correct options there are</c> of the correct-answer marks when that is
    /// above zero. Subtracting the wrong choices is what stops "tick everything" from scoring well. When it is not above zero the
    /// answer is plainly wrong and carries the incorrect-answer marks, so negative marking still bites on a bad guess.
    /// </remarks>
    /// <param name="exam">The exam, which holds the marking scheme.</param>
    /// <param name="question">The question from the question bank, answer key included.</param>
    /// <param name="chosenOptionIds">The options the candidate chose; empty when they chose none.</param>
    /// <exception cref="ExamContentUnavailableError">A chosen option is not one of the question's options.</exception>
    public static QuestionMark Mark(ExamSnapshot exam, QuestionSnapshot question, IReadOnlyCollection<Guid> chosenOptionIds) =>
        Mark(exam, question, chosenOptionIds, answerText: null);

    /// <summary>
    /// Marks one question against what the candidate answered: the options they chose, or, for a text question, the answer they typed.
    /// A typed answer is right when it matches one of the question's accepted answers (<see cref="TypedAnswer.Matches"/>), and there is no
    /// partial credit for it: it is right or wrong, and a blank one is unanswered.
    /// </summary>
    /// <param name="exam">The exam, which holds the marking scheme.</param>
    /// <param name="question">The question from the question bank, answer key included.</param>
    /// <param name="chosenOptionIds">The options the candidate chose; empty for a text question or when they chose none.</param>
    /// <param name="answerText">What the candidate typed to a text question; null for one answered by choosing options.</param>
    /// <exception cref="ExamContentUnavailableError">A chosen option is not one of the question's options.</exception>
    public static QuestionMark Mark(ExamSnapshot exam, QuestionSnapshot question, IReadOnlyCollection<Guid> chosenOptionIds, string? answerText)
    {
        if (question.IsTextAnswer)
            return MarkTyped(exam, question, answerText);

        if (chosenOptionIds.Count == 0)
            return new QuestionMark(AnswerVerdict.Unanswered, exam.UnattemptedMarks);

        var chosen = chosenOptionIds.ToHashSet();
        var known = question.Options.Select(o => o.Id).ToHashSet();
        // A saved answer naming an option the question no longer has cannot be marked either way; failing loudly beats guessing.
        if (!chosen.IsSubsetOf(known))
            throw new ExamContentUnavailableError();

        var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
        if (chosen.SetEquals(correct))
            return new QuestionMark(AnswerVerdict.Correct, exam.CorrectMarks);

        if (exam.PartialCredit && correct.Count > 1)
        {
            var net = chosen.Count(correct.Contains) - chosen.Count(id => !correct.Contains(id));
            if (net > 0)
            {
                // Rounded to the hundredths a mark can have, so a share of 1/3 does not produce a total nobody can add up by hand.
                var share = decimal.Round(exam.CorrectMarks * net / correct.Count, 2, MidpointRounding.AwayFromZero);
                return new QuestionMark(AnswerVerdict.Partial, share);
            }
        }

        return new QuestionMark(AnswerVerdict.Wrong, exam.IncorrectMarks);
    }

    /// <summary>A typed answer is right or wrong, never partly right: a blank one is unanswered, and every other is compared with the accepted answers.</summary>
    private static QuestionMark MarkTyped(ExamSnapshot exam, QuestionSnapshot question, string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
            return new QuestionMark(AnswerVerdict.Unanswered, exam.UnattemptedMarks);

        return TypedAnswer.Matches(answerText, question.AcceptedAnswers ?? [])
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
        var byQuestion = answers.ToDictionary(a => a.QuestionId);
        decimal score = 0;
        decimal max = 0;

        foreach (var questionId in exam.Sections.SelectMany(s => s.QuestionIds))
        {
            // A question the bank no longer returns cannot be marked either way; failing loudly beats
            // quietly awarding or withholding marks for it.
            if (!questions.TryGetValue(questionId, out var question))
                throw new ExamContentUnavailableError();

            max += exam.CorrectMarks;
            var answer = byQuestion.GetValueOrDefault(questionId);
            score += Mark(exam, question, answer?.SelectedOptionIds ?? [], answer?.AnswerText).Marks;
        }

        return new AttemptScore(score, max);
    }
}
