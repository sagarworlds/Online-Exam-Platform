namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// The departure from the exam page at which a candidate is first warned, separate from the limit at which the attempt ends (FR-22).
/// Requirements section 8 warns at the third departure and ends at the author's limit, so a candidate is warned before the attempt is
/// cut off. An author sets only the limit; the warning point is worked out from it. It lives in the shared kernel because the authoring
/// notice and the runtime's warnings both need the same rule, and no module may reference another module's domain.
/// </summary>
public static class FocusWarningPoint
{
    /// <summary>The departure from which a candidate is warned, when the limit allows it (requirements section 8, tabSwitchWarnLimit).</summary>
    public const int WarnFrom = 3;

    /// <summary>
    /// The departure from which the candidate is warned. With a limit of 4 or more that is <see cref="WarnFrom"/>. A smaller limit moves it
    /// down, so a candidate with a limit of 2 is still warned once before the attempt ends. With a limit of 1 the only departure is the one
    /// that ends the attempt, so the warning point is 1.
    /// </summary>
    /// <param name="terminateLimit">The author's limit: how many departures end the attempt. Zero or less means the exam does not watch.</param>
    /// <returns>The departure number from which to warn, or 0 when the exam does not watch.</returns>
    public static int For(int terminateLimit) =>
        terminateLimit <= 0 ? 0 : Math.Max(1, Math.Min(WarnFrom, terminateLimit - 1));
}
