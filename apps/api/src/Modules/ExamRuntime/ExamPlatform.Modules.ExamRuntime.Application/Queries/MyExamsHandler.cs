using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Lists the published exams a candidate is enrolled in, with where each one stands for them (FR-16).</summary>
public sealed class MyExamsHandler(
    IEnrollments enrollments,
    IExamCatalog catalog,
    IAttemptRepository attempts,
    IExtraAttemptGrantRepository grants,
    IAttemptRequestRepository requests,
    Clock clock)
{
    /// <summary>Returns the exams the user accepted an invitation to, soonest first, each with every attempt they have made at it.</summary>
    /// <param name="userId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<MyExamDto>> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var examIds = await enrollments.GetEnrolledExamIdsAsync(userId, cancellationToken);
        var exams = await catalog.FindPublishedAsync(examIds, cancellationToken);
        var attemptsByExam = (await attempts.ListForCandidateAsync(userId, cancellationToken))
            .GroupBy(a => a.ExamId)
            .ToDictionary(group => group.Key, group => group.OrderBy(a => a.Number).ToList());
        var grantCounts = await grants.CountsForCandidateAsync(userId, cancellationToken);
        var latestRequests = await requests.LatestForCandidateAsync(userId, cancellationToken);
        var nowUtc = clock.UtcNow;

        return exams
            .OrderBy(e => e.StartUtc)
            .Select(e =>
            {
                var made = attemptsByExam.GetValueOrDefault(e.Id) ?? [];
                var latest = made.Count > 0 ? made[^1] : null;
                var granted = grantCounts.GetValueOrDefault(e.Id);
                var state = StateOf(e, nowUtc);
                var request = latestRequests.GetValueOrDefault(e.Id);

                return new MyExamDto(
                    e.Id,
                    e.Name,
                    e.Description,
                    e.StartUtc,
                    e.EndUtc,
                    e.LateEntryDeadlineUtc,
                    e.DurationSeconds,
                    e.Sections.Sum(s => s.QuestionIds.Count + (s.DrawRules?.Sum(r => r.Count) ?? 0)),
                    state,
                    latest?.Id,
                    latest?.Status,
                    latest?.Score,
                    latest?.MaxScore,
                    AttemptAllowance.Allowed(e.MaxAttempts, granted),
                    made.Count,
                    // The window is open, nothing is in progress (that one is resumed, not followed by another), and one is left.
                    state == MyExamState.Open && latest is not { Status: AttemptStatus.InProgress } && AttemptAllowance.CanStartAnother(e.MaxAttempts, made.Count, granted),
                    made.Select(ExamCandidateRows.Summary).ToList(),
                    // The same moment an administrator could grant one, and not while a request of theirs already waits for an answer.
                    state == MyExamState.Open
                        && latest is not { Status: AttemptStatus.InProgress }
                        && AttemptAllowance.CanGrant(e.MaxAttempts, made.Count, granted)
                        && request is not { Status: AttemptRequestStatus.Pending },
                    request is null ? null : AttemptRequestDtoFactory.ForCandidate(request),
                    new ExamRulesDto(e.CorrectMarks, e.IncorrectMarks, e.UnattemptedMarks, e.PartialCredit, e.SectionLockEnabled, e.Sections.Count, e.ContentProtection, e.FocusViolationLimit));
            })
            .ToList();
    }

    private static MyExamState StateOf(ExamSnapshot exam, DateTime nowUtc) =>
        nowUtc < exam.StartUtc ? MyExamState.NotOpen
        : ExamWindow.CanStart(exam, nowUtc) ? MyExamState.Open
        : MyExamState.Closed;
}
