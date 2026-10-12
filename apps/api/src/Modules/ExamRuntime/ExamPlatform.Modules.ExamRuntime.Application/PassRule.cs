namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Whether a score passes the exam (FR-34). The mark is a percentage of the total marks, so a score passes when it is at least that share of
/// the marks available. One rule, used by the result page and the certificate, so the two can never disagree about a pass.
/// </summary>
public static class PassRule
{
    /// <summary>Whether the score passes, or <see langword="null"/> when no pass can be judged.</summary>
    /// <param name="score">The marks scored; may be negative with negative marking.</param>
    /// <param name="maxScore">The marks available.</param>
    /// <param name="passingPercentage">The exam's pass mark, or null when the exam has none.</param>
    /// <returns>
    /// <see langword="null"/> when the exam has no pass mark, or when the exam has no marks to take a percentage of, so nothing is
    /// called a pass or a fail; otherwise whether the score reaches the mark.
    /// </returns>
    public static bool? Of(decimal score, decimal maxScore, int? passingPercentage)
    {
        if (passingPercentage is not { } mark || maxScore <= 0)
            return null;

        // Compared as score * 100 against mark * maxScore, so no division rounds a borderline score across the mark.
        return score * 100m >= mark * maxScore;
    }
}
