using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>The marks an attempt earned and the most it could have earned.</summary>
/// <param name="Score">The marks scored.</param>
/// <param name="MaxScore">The marks available.</param>
public readonly record struct AttemptScore(decimal Score, decimal MaxScore);

/// <summary>Marks an attempt against the exam's marking scheme. Pure: everything it needs is passed in.</summary>
public static class AttemptScorer
{
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
        var chosen = answers.ToDictionary(a => a.QuestionId, a => a.SelectedOptionId);
        decimal score = 0;
        decimal max = 0;

        foreach (var questionId in exam.Sections.SelectMany(s => s.QuestionIds))
        {
            // A question the bank no longer returns cannot be marked either way; failing loudly beats
            // quietly awarding or withholding marks for it.
            if (!questions.TryGetValue(questionId, out var question))
                throw new ExamContentUnavailableError();

            max += exam.CorrectMarks;

            if (!chosen.TryGetValue(questionId, out var optionId))
            {
                score += exam.UnattemptedMarks;
                continue;
            }

            var option = question.Options.FirstOrDefault(o => o.Id == optionId)
                ?? throw new ExamContentUnavailableError();
            score += option.IsCorrect ? exam.CorrectMarks : exam.IncorrectMarks;
        }

        return new AttemptScore(score, max);
    }
}
