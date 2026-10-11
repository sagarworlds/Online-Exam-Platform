using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Analytics.Domain.Exceptions;

/// <summary>
/// A tally was given a negative count. A count of questions cannot be negative, so the figures it came from are wrong and no accuracy may
/// be shown from them.
/// </summary>
public sealed class InvalidAnswerTallyError(string countName) : DomainException($"The {countName} count of an answer tally cannot be negative.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_answer_tally";

    /// <summary>The name of the count that was negative, e.g. <c>correct</c>. Safe to surface: a field name, never a value.</summary>
    public string CountName { get; } = countName;
}
