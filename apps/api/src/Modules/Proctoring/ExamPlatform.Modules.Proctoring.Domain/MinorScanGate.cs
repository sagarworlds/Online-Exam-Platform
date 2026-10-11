namespace ExamPlatform.Modules.Proctoring.Domain;

/// <summary>
/// Whether the risk score may read attempts sat by candidates under 18 (exam-platform-requirements.md section 7.2). Off until counsel's
/// opinion is recorded on issue #11: the configuration switch <c>Proctoring:MinorsScanEnabled</c> defaults to off, and only an operator
/// who has the opinion turns it on.
/// </summary>
/// <param name="MinorsScanEnabled">True when the score may read attempts by minors; false excludes them.</param>
public sealed record MinorScanPolicy(bool MinorsScanEnabled);

/// <summary>The attempts a scan will read, and how many it left out because the candidate was a minor.</summary>
/// <param name="ToScan">The attempts to read, in the order they were given.</param>
/// <param name="ExcludedUnder18">How many attempts were left out. Never silent: the count is returned to the caller and shown to staff.</param>
public sealed record MinorScanSelection(IReadOnlyList<Guid> ToScan, int ExcludedUnder18);

/// <summary>Decides which finished attempts a scan may read under the minors policy (FR-27, section 7.2).</summary>
public static class MinorScanGate
{
    /// <summary>
    /// Splits the finished attempts into those a scan may read and those it must leave out.
    /// </summary>
    /// <remarks>
    /// With the switch on, nothing is excluded. With it off, an attempt sat by a minor is left out before any of its answers are read,
    /// so no minor's answers are processed at all while the switch is off. The excluded attempts are counted, not dropped silently.
    /// </remarks>
    /// <param name="attemptIds">Every finished attempt at the exam.</param>
    /// <param name="attemptsSatAsMinor">The attempts whose candidate was under 18 on the day each started.</param>
    /// <param name="policy">The minors policy.</param>
    /// <returns>The attempts to read and the number excluded.</returns>
    public static MinorScanSelection Select(IReadOnlyList<Guid> attemptIds, IReadOnlySet<Guid> attemptsSatAsMinor, MinorScanPolicy policy)
    {
        if (policy.MinorsScanEnabled)
        {
            return new MinorScanSelection(attemptIds, 0);
        }

        var toScan = attemptIds.Where(id => !attemptsSatAsMinor.Contains(id)).ToList();
        return new MinorScanSelection(toScan, attemptIds.Count - toScan.Count);
    }
}
