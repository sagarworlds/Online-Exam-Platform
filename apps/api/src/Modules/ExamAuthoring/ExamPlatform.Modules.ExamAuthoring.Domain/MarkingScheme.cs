using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.Domain;

/// <summary>Marks awarded for a correct, an incorrect and an unattempted question.</summary>
/// <param name="CorrectMarks">Marks for a correct answer.</param>
/// <param name="IncorrectMarks">Marks for an incorrect answer; zero or negative (negative marking).</param>
/// <param name="UnattemptedMarks">Marks for a question left unanswered; zero or negative.</param>
public record MarkingScheme(decimal CorrectMarks, decimal IncorrectMarks, decimal UnattemptedMarks)
{
    /// <summary>The most marks any one question can be worth, or cost: a guard against a slipped digit.</summary>
    public const decimal LargestMagnitude = 100m;

    /// <summary>The most decimal places a mark can have; quarter and half marks are common, thousandths are not.</summary>
    public const int MostDecimalPlaces = 2;

    public MarkingScheme() : this(1m, 0m, 0m) { }

    /// <summary>
    /// Checks the scheme before it is stored. A correct answer must earn something and the other two must never earn
    /// anything, otherwise skipping or guessing wrong could beat answering right.
    /// </summary>
    /// <exception cref="InvalidExamConfigError">A mark is out of range, has too many decimal places, or has the wrong sign.</exception>
    public void EnsureValid()
    {
        if (CorrectMarks is <= 0m or > LargestMagnitude)
            throw new InvalidExamConfigError($"Marks for a correct answer must be more than 0 and at most {LargestMagnitude}.");

        if (IncorrectMarks is > 0m or < -LargestMagnitude)
            throw new InvalidExamConfigError($"Marks for an incorrect answer must be from -{LargestMagnitude} to 0.");

        if (UnattemptedMarks is > 0m or < -LargestMagnitude)
            throw new InvalidExamConfigError($"Marks for an unattempted question must be from -{LargestMagnitude} to 0.");

        if (HasTooManyDecimals(CorrectMarks) || HasTooManyDecimals(IncorrectMarks) || HasTooManyDecimals(UnattemptedMarks))
            throw new InvalidExamConfigError($"Marks can have at most {MostDecimalPlaces} decimal places.");
    }

    private static bool HasTooManyDecimals(decimal marks) => decimal.Round(marks, MostDecimalPlaces) != marks;
}
