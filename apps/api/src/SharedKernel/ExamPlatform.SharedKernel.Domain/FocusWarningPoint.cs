namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// The departure from the exam page at which a candidate is first warned, separate from the limit at which the attempt ends (FR-22).
/// Requirements section 8 warns at the third departure and ends at the author's limit, so a candidate is warned before the attempt is cut
/// off. An author may set the warning point; when they do not, it is worked out from the limit. It lives in the shared kernel because the
/// authoring notice and the runtime's warnings both need the same rule, and no module may reference another module's domain.
/// </summary>
public static class FocusWarningPoint
{
    /// <summary>The departure from which a candidate is warned by default (requirements section 8, tabSwitchWarnLimit).</summary>
    public const int WarnFrom = 3;

    /// <summary>
    /// The departure from which the candidate is warned when the author has not set one. With a limit of 4 or more that is
    /// <see cref="WarnFrom"/>. A smaller limit moves it down, so a candidate with a limit of 2 is still warned once before the attempt ends.
    /// With a limit of 1 the only departure is the one that ends the attempt, so the warning point is 1.
    /// </summary>
    /// <param name="terminateLimit">The author's limit: how many departures end the attempt. Zero or less means the exam does not watch.</param>
    /// <returns>The departure number from which to warn, or 0 when the exam does not watch.</returns>
    public static int For(int terminateLimit) => Effective(terminateLimit, configuredWarnAt: null);

    /// <summary>
    /// The departure from which the candidate is warned: the author's setting, or the default when they have not set one. A setting that is
    /// later than the last departure before the limit is brought back to that departure, so the warning is always given before the attempt
    /// ends. The setting is validated when the author makes it, so this only matters for a limit changed without it.
    /// </summary>
    /// <param name="terminateLimit">The limit: how many departures end the attempt. Zero or less means the exam does not watch.</param>
    /// <param name="configuredWarnAt">The departure the author chose to warn from, or null for the default.</param>
    /// <returns>The departure number from which to warn, or 0 when the exam does not watch.</returns>
    public static int Effective(int terminateLimit, int? configuredWarnAt)
    {
        if (terminateLimit <= 0)
            return 0;

        var wanted = configuredWarnAt ?? WarnFrom;
        return Math.Max(1, Math.Min(wanted, LatestWarnAt(terminateLimit)));
    }

    /// <summary>
    /// The latest departure a candidate may be warned from: the one before the departure that ends the attempt, so they are warned before
    /// they are cut off. With a limit of 1 there is no earlier departure, so the only one, the first, is the latest.
    /// </summary>
    /// <param name="terminateLimit">The limit: how many departures end the attempt.</param>
    public static int LatestWarnAt(int terminateLimit) => Math.Max(1, terminateLimit - 1);

    /// <summary>
    /// Whether an author may set the warning point to this departure: from the first departure to <see cref="LatestWarnAt"/>, and only for an
    /// exam that watches for departures.
    /// </summary>
    /// <param name="warnAt">The departure to warn from.</param>
    /// <param name="terminateLimit">The limit. The warning point cannot be set while the exam does not watch.</param>
    public static bool IsAllowed(int warnAt, int terminateLimit) =>
        terminateLimit > 0 && warnAt >= 1 && warnAt <= LatestWarnAt(terminateLimit);
}
