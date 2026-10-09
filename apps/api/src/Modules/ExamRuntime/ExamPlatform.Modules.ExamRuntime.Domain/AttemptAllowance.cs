namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// How many attempts a candidate may make at an exam. The exam's author sets how many every candidate has; each
/// <see cref="ExtraAttemptGrant"/> adds one on top, for that candidate alone. Pure arithmetic on counts, so the rule lives in one
/// place for starting an attempt, granting one, and every screen that shows it.
/// </summary>
public static class AttemptAllowance
{
    /// <summary>How many attempts the candidate may make in all.</summary>
    /// <param name="perCandidate">The attempts every enrolled candidate has at this exam, as the author set it.</param>
    /// <param name="grants">How many extra attempts have been granted to them at this exam.</param>
    public static int Allowed(int perCandidate, int grants) => perCandidate + grants;

    /// <summary>Whether they may begin another attempt: they have not yet used all they are allowed.</summary>
    /// <param name="perCandidate">The attempts every enrolled candidate has at this exam.</param>
    /// <param name="attemptsMade">How many attempts they have started, finished or not.</param>
    /// <param name="grants">How many extra attempts have been granted to them.</param>
    public static bool CanStartAnother(int perCandidate, int attemptsMade, int grants) => attemptsMade < Allowed(perCandidate, grants);

    /// <summary>
    /// Whether another extra attempt may be granted: only once they have used exactly the attempts they hold. Not before, so a
    /// double click, or two administrators acting on the same request, cannot stockpile attempts the candidate has not even asked
    /// for yet; and not after, because a grant adds one attempt, so for a candidate already over the limit (see
    /// <see cref="IsOverLimit"/>) it would change nothing.
    /// </summary>
    /// <param name="perCandidate">The attempts every enrolled candidate has at this exam.</param>
    /// <param name="attemptsMade">How many attempts they have started.</param>
    /// <param name="grants">How many extra attempts have been granted to them.</param>
    public static bool CanGrant(int perCandidate, int attemptsMade, int grants) => attemptsMade == Allowed(perCandidate, grants);

    /// <summary>
    /// Whether they have made more attempts than they are now allowed. Only possible when the author lowers the exam's limit after
    /// they sat it: the attempts already made stay, and no further one can start until the limit is raised.
    /// </summary>
    /// <param name="perCandidate">The attempts every enrolled candidate has at this exam.</param>
    /// <param name="attemptsMade">How many attempts they have started.</param>
    /// <param name="grants">How many extra attempts have been granted to them.</param>
    public static bool IsOverLimit(int perCandidate, int attemptsMade, int grants) => attemptsMade > Allowed(perCandidate, grants);
}
