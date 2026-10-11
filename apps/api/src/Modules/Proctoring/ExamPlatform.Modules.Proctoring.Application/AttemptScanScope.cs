using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Proctoring.Domain;

namespace ExamPlatform.Modules.Proctoring.Application;

/// <summary>The finished attempts a scan may read, and how many were left out under the minors policy.</summary>
/// <param name="AttemptIds">The attempts to read.</param>
/// <param name="ExcludedUnder18">How many finished attempts were left out because the candidate was under 18.</param>
public sealed record FinishedAttemptScope(IReadOnlyList<Guid> AttemptIds, int ExcludedUnder18);

/// <summary>
/// Decides which finished attempts of an exam the risk score may read (FR-27, section 7.2). The scan and the review queue both use it,
/// so the count of attempts left out is the same wherever staff look at it.
/// </summary>
/// <remarks>
/// The attempt list comes from Exam Runtime without answers, and the ages come from Identity, so an excluded attempt's answers are
/// never read. When minors may be scanned the age lookup is skipped altogether.
/// </remarks>
public sealed class AttemptScanScope(IAttemptSignalSource signals, ICandidateAgeDirectory ages, MinorScanPolicy policy)
{
    /// <summary>Works out the scope of a scan of one exam.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempts to read and the number left out.</returns>
    public async Task<FinishedAttemptScope> ResolveAsync(Guid examId, CancellationToken cancellationToken)
    {
        var finished = await signals.ListFinishedAttemptRefsAsync(examId, cancellationToken);
        var ids = finished.Select(f => f.AttemptId).ToList();

        IReadOnlySet<Guid> satAsMinor = new HashSet<Guid>();
        if (!policy.MinorsScanEnabled && finished.Count > 0)
        {
            var starts = finished.Select(f => new AttemptStart(f.AttemptId, f.CandidateId, f.StartedAtUtc)).ToList();
            satAsMinor = await ages.FindAttemptsSatAsMinorAsync(starts, cancellationToken);
        }

        var selection = MinorScanGate.Select(ids, satAsMinor, policy);
        return new FinishedAttemptScope(selection.ToScan, selection.ExcludedUnder18);
    }
}
