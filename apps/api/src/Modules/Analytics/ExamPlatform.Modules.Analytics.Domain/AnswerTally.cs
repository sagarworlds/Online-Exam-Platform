using ExamPlatform.Modules.Analytics.Domain.Exceptions;

namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>
/// How a candidate's answers fell across one or more questions: the counts that <see cref="PerformanceMeasures.Accuracy"/> is worked from.
/// Adding tallies together gives the figures for a group, such as one subject across several exams.
/// </summary>
/// <remarks>
/// Build a tally with <see cref="Of"/>, which refuses a negative count, rather than the constructor, so a bad count cannot reach an accuracy.
/// </remarks>
/// <param name="Correct">Questions answered fully correctly.</param>
/// <param name="Wrong">Questions answered wrongly.</param>
/// <param name="Partial">Multiple-answer questions answered partly right.</param>
/// <param name="Unanswered">Questions left unanswered.</param>
public readonly record struct AnswerTally(int Correct, int Wrong, int Partial, int Unanswered)
{
    /// <summary>The tally of nothing, the starting point for adding up a group.</summary>
    public static AnswerTally Empty => default;

    /// <summary>How many questions were answered at all: correct, wrong or partly right.</summary>
    public int Answered => Correct + Wrong + Partial;

    /// <summary>The accuracy over the answered questions, in percent; null when nothing was answered.</summary>
    public decimal? Accuracy => PerformanceMeasures.Accuracy(Correct, Wrong, Partial);

    /// <summary>Builds a tally from its four counts.</summary>
    /// <param name="correct">Questions answered fully correctly; not negative.</param>
    /// <param name="wrong">Questions answered wrongly; not negative.</param>
    /// <param name="partial">Multiple-answer questions answered partly right; not negative.</param>
    /// <param name="unanswered">Questions left unanswered; not negative.</param>
    /// <returns>The tally.</returns>
    /// <exception cref="InvalidAnswerTallyError">A count is negative.</exception>
    public static AnswerTally Of(int correct, int wrong, int partial, int unanswered)
    {
        RequireNonNegative(correct, nameof(correct));
        RequireNonNegative(wrong, nameof(wrong));
        RequireNonNegative(partial, nameof(partial));
        RequireNonNegative(unanswered, nameof(unanswered));
        return new AnswerTally(correct, wrong, partial, unanswered);
    }

    /// <summary>The sum of this tally and another.</summary>
    /// <param name="other">The tally to add.</param>
    /// <returns>A tally with each count added.</returns>
    public AnswerTally Add(AnswerTally other) =>
        new(Correct + other.Correct, Wrong + other.Wrong, Partial + other.Partial, Unanswered + other.Unanswered);

    private static void RequireNonNegative(int count, string countName)
    {
        if (count < 0)
            throw new InvalidAnswerTallyError(countName);
    }
}
