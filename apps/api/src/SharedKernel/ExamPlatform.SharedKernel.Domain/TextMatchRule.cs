namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// How strictly a typed answer is compared with a text question's accepted answers (FR-5). The author chooses it for each question and it is
/// kept with every version of the question, so a candidate is always marked by the rule that was in force when they sat the exam.
/// </summary>
/// <remarks>
/// Appended-only, and stored by name: a new rule is added at the end and an existing one never changes what it means, because stored
/// attempts are re-marked from their stored text whenever a result is shown.
/// </remarks>
public enum TextMatchRule
{
    /// <summary>
    /// Capital letters and extra spaces do not matter, and nothing else is forgiven. What every text question used before the choice
    /// existed, so a question written then is marked exactly as it was.
    /// </summary>
    Exact = 0,

    /// <summary>
    /// As <see cref="Exact"/>, and also forgiving of the small differences that do not change an answer: stray punctuation ("Paris." for
    /// "Paris", "U.S.A." for "USA"), a hyphen between two words ("well-known" for "well known") and accents on Latin letters ("cafe" for
    /// "café"). Signs and symbols that can change a number or a formula ("+", "-", "=", "/", "%", a decimal point) are kept.
    /// </summary>
    Lenient = 1,
}
