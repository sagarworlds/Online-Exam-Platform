using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Invite.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>Builds what staff are shown about one candidate of an exam, so listing and granting describe a candidate the same way.</summary>
public static class ExamCandidateRows
{
    /// <summary>Whether nobody can start an attempt at the exam any more.</summary>
    /// <param name="exam">The exam.</param>
    /// <param name="nowUtc">The current instant.</param>
    public static bool IsWindowClosed(ExamSnapshot exam, DateTime nowUtc) => nowUtc > ExamWindow.LastStartUtc(exam);

    /// <summary>Describes one candidate.</summary>
    /// <param name="exam">The exam.</param>
    /// <param name="candidate">The enrolled candidate.</param>
    /// <param name="attempts">Their attempts at the exam, in any order.</param>
    /// <param name="grants">How many extra attempts they have been granted.</param>
    /// <param name="nowUtc">The current instant.</param>
    public static ExamCandidateDto For(ExamSnapshot exam, EnrolledCandidate candidate, IEnumerable<Attempt> attempts, int grants, DateTime nowUtc)
    {
        var ordered = attempts.OrderBy(a => a.Number).ToList();

        return new ExamCandidateDto(
            candidate.UserId,
            candidate.Email,
            AttemptAllowance.Allowed(exam.MaxAttempts, grants),
            ordered.Count,
            AttemptAllowance.CanGrant(exam.MaxAttempts, ordered.Count, grants) && !IsWindowClosed(exam, nowUtc),
            ordered.Select(Summary).ToList());
    }

    /// <summary>Summarises one attempt for a list.</summary>
    /// <param name="attempt">The attempt.</param>
    public static AttemptSummaryDto Summary(Attempt attempt) =>
        new(attempt.Id, attempt.Number, attempt.Status, attempt.StartedAtUtc, attempt.SubmittedAtUtc, attempt.AutoSubmitted, attempt.Score, attempt.MaxScore);
}
