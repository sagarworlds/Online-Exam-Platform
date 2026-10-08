using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Builds the staff view of reported problems, looking each exam, its candidates and attempts, and each question up once however many
/// reports name them: a question many candidates report is the usual case.
/// </summary>
public sealed class IssueReportDtoFactory(IExamCatalog catalog, IExamRoster roster, IAttemptRepository attempts, IQuestionBank questionBank)
{
    /// <summary>Maps reports to DTOs, keeping their order.</summary>
    /// <param name="reports">The reports to describe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<IssueReportDto>> CreateAsync(IReadOnlyList<IssueReport> reports, CancellationToken cancellationToken)
    {
        if (reports.Count == 0)
            return [];

        var names = new Dictionary<Guid, string?>();
        var emails = new Dictionary<Guid, IReadOnlyDictionary<Guid, string>>();
        var numbers = new Dictionary<Guid, int>();

        foreach (var examId in reports.Select(r => r.ExamId).Distinct())
        {
            names[examId] = (await catalog.FindAsync(examId, cancellationToken))?.Name;
            emails[examId] = (await roster.GetEnrolledCandidatesAsync(examId, cancellationToken)).ToDictionary(c => c.UserId, c => c.Email);
            foreach (var attempt in await attempts.ListForExamAsync(examId, cancellationToken))
                numbers[attempt.Id] = attempt.Number;
        }

        var questionIds = reports.Where(r => r.QuestionId is not null).Select(r => r.QuestionId!.Value).Distinct().ToList();
        var questions = questionIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await questionBank.GetAsync(questionIds, cancellationToken)).ToDictionary(q => q.Id, q => q.Text);

        return reports
            .Select(r => new IssueReportDto(
                r.Id,
                r.ExamId,
                names[r.ExamId],
                r.AttemptId,
                numbers.TryGetValue(r.AttemptId, out var number) ? number : null,
                r.CandidateId,
                emails[r.ExamId].GetValueOrDefault(r.CandidateId),
                r.QuestionId,
                r.QuestionId is { } questionId ? questions.GetValueOrDefault(questionId) : null,
                r.Category,
                r.Message,
                r.ReportedAtUtc,
                r.Status,
                r.ResolvedAtUtc,
                r.ResolutionNote))
            .ToList();
    }

    /// <summary>The candidate's own confirmation of a report they just made.</summary>
    /// <param name="report">The report.</param>
    public static MyIssueReportDto ForCandidate(IssueReport report) =>
        new(report.Id, report.Category, report.QuestionId, report.Message, report.ReportedAtUtc);
}
