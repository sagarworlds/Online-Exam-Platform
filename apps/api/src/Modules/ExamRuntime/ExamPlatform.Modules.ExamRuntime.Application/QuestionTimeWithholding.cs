using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Identity.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Decides which attempts have their time per question withheld: it is not stored when the page reports it, and it is not shown back to the
/// candidate (FR-36, exam-platform-requirements.md section 7). Used on the write path by <see cref="Commands.RecordQuestionTimeHandler"/> and on
/// the read path by <see cref="CandidateResultReader"/>, so one rule decides both.
/// </summary>
public sealed class QuestionTimeWithholding(ICandidateAgeDirectory ages, QuestionTimePolicy policy)
{
    /// <summary>Of the given attempts, the ids whose time per question is withheld.</summary>
    /// <remarks>
    /// With the switch on nothing is withheld and the age lookup is skipped altogether. With it off, the candidates' ages are read from Identity,
    /// which is the one owner of dates of birth and of the under-18 rule; nothing here keeps a copy of a birth date.
    /// </remarks>
    /// <param name="attempts">The attempts to check, each with its candidate and start time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ids of the attempts whose time is withheld; empty when none is.</returns>
    public async Task<IReadOnlySet<Guid>> FindWithheldAsync(IReadOnlyCollection<Attempt> attempts, CancellationToken cancellationToken)
    {
        if (policy.MinorsEnabled || attempts.Count == 0)
            return new HashSet<Guid>();

        // Decision point: fail closed. The directory reports a candidate with no date of birth on record as a minor, because the platform
        // cannot show they are an adult, so an unknown age withholds the time exactly as a known minor's does. Only an attempt the directory
        // does not list, which means its candidate is known to have been 18 or over when it started, keeps its time.
        var starts = attempts.Select(a => new AttemptStart(a.Id, a.CandidateId, a.StartedAtUtc)).ToList();
        return await ages.FindAttemptsSatAsMinorAsync(starts, cancellationToken);
    }
}
