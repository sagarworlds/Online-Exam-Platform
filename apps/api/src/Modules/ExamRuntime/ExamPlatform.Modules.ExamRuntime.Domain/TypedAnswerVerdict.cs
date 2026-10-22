namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// What the AI evaluator decided about a typed answer: right or wrong, never partly right, as a typed answer is marked everywhere else.
/// An answer nobody has evaluated has no verdict at all, which is different from either of these.
/// </summary>
public enum TypedAnswerVerdict
{
    /// <summary>The answer says what the question's accepted answers say, in different words if need be.</summary>
    Correct,

    /// <summary>The answer does not say what the question's accepted answers say.</summary>
    Wrong,
}
