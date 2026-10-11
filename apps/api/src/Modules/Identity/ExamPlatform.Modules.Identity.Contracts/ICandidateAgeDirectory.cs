namespace ExamPlatform.Modules.Identity.Contracts;

/// <summary>
/// Whether an attempt was sat by a candidate who was a minor when it started (exam-platform-requirements.md section 7). Identity holds
/// the dates of birth and owns the rule for "under 18", so other modules ask this question rather than reading a birth date. Consumed
/// through this Contracts project only.
/// </summary>
public interface ICandidateAgeDirectory
{
    /// <summary>
    /// Of the given attempts, the ones whose candidate was under 18 on the day each attempt started.
    /// </summary>
    /// <remarks>
    /// A candidate whose date of birth cannot be found is counted as a minor. The platform cannot show they are an adult, and the
    /// minors rule is the one that must fail safe.
    /// </remarks>
    /// <param name="attempts">The attempts to check, each with its candidate and start time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids of the attempts sat by a minor; empty when every candidate was an adult.</returns>
    Task<IReadOnlySet<Guid>> FindAttemptsSatAsMinorAsync(IReadOnlyCollection<AttemptStart> attempts, CancellationToken cancellationToken);
}

/// <summary>An attempt to check: which attempt, who sat it and when it started (UTC).</summary>
/// <param name="AttemptId">The attempt.</param>
/// <param name="CandidateId">The candidate who sat it, who is the user whose date of birth is read.</param>
/// <param name="StartedAtUtc">When the attempt started, by the server's clock.</param>
public sealed record AttemptStart(Guid AttemptId, Guid CandidateId, DateTime StartedAtUtc);
