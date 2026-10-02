using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Lists the published exams a candidate is enrolled in, with where each one stands for them (FR-16).</summary>
public sealed class MyExamsHandler(IEnrollments enrollments, IExamCatalog catalog, IAttemptRepository attempts, Clock clock)
{
    /// <summary>Returns the exams the user accepted an invitation to, soonest first.</summary>
    /// <param name="userId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<MyExamDto>> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var examIds = await enrollments.GetEnrolledExamIdsAsync(userId, cancellationToken);
        var exams = await catalog.FindPublishedAsync(examIds, cancellationToken);
        var attemptsByExam = (await attempts.ListForCandidateAsync(userId, cancellationToken)).ToDictionary(a => a.ExamId);
        var nowUtc = clock.UtcNow;

        return exams
            .OrderBy(e => e.StartUtc)
            .Select(e =>
            {
                var attempt = attemptsByExam.GetValueOrDefault(e.Id);
                return new MyExamDto(
                    e.Id,
                    e.Name,
                    e.Description,
                    e.StartUtc,
                    e.EndUtc,
                    e.LateEntryDeadlineUtc,
                    e.DurationSeconds,
                    e.Sections.Sum(s => s.QuestionIds.Count),
                    StateOf(e, nowUtc),
                    attempt?.Id,
                    attempt?.Status,
                    attempt?.Score,
                    attempt?.MaxScore);
            })
            .ToList();
    }

    private static MyExamState StateOf(ExamSnapshot exam, DateTime nowUtc) =>
        nowUtc < exam.StartUtc ? MyExamState.NotOpen
        : ExamWindow.CanStart(exam, nowUtc) ? MyExamState.Open
        : MyExamState.Closed;
}
