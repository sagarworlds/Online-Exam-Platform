namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// How many attempts a candidate may make at an exam. Everyone has one; each <see cref="ExtraAttemptGrant"/> adds one. Pure
/// arithmetic on counts, so the rule lives in one place for starting an attempt, granting one, and every screen that shows it.
/// </summary>
public static class AttemptAllowance
{
    /// <summary>The attempts every enrolled candidate has before any is granted.</summary>
    public const int Standard = 1;

    /// <summary>How many attempts the candidate may make in all.</summary>
    /// <param name="grants">How many extra attempts have been granted to them at this exam.</param>
    public static int Allowed(int grants) => Standard + grants;

    /// <summary>Whether they may begin another attempt: they have not yet used all they are allowed.</summary>
    /// <param name="attemptsMade">How many attempts they have started, finished or not.</param>
    /// <param name="grants">How many extra attempts have been granted to them.</param>
    public static bool CanStartAnother(int attemptsMade, int grants) => attemptsMade < Allowed(grants);

    /// <summary>
    /// Whether another extra attempt may be granted: only once they have used every attempt they hold, so a double click, or two
    /// administrators acting on the same request, cannot stockpile attempts the candidate has not even asked for yet.
    /// </summary>
    /// <param name="attemptsMade">How many attempts they have started.</param>
    /// <param name="grants">How many extra attempts have been granted to them.</param>
    public static bool CanGrant(int attemptsMade, int grants) => !CanStartAnother(attemptsMade, grants);
}
