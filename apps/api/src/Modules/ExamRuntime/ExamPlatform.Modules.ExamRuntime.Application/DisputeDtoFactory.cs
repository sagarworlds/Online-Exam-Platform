using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Builds the staff view of disputes, looking each exam, its candidates and attempts, and each question up once however many disputes
/// name them: a question many candidates dispute is the usual case.
/// </summary>
public sealed class DisputeDtoFactory(IExamCatalog catalog, IExamRoster roster, IAttemptRepository attempts, IQuestionBank questionBank)
{
    /// <summary>Maps disputes to DTOs, keeping their order.</summary>
    /// <param name="disputes">The disputes to report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<DisputeDto>> CreateAsync(IReadOnlyList<Dispute> disputes, CancellationToken cancellationToken)
    {
        if (disputes.Count == 0)
            return [];

        var names = new Dictionary<Guid, string?>();
        var emails = new Dictionary<Guid, IReadOnlyDictionary<Guid, string>>();
        var numbers = new Dictionary<Guid, int>();

        foreach (var examId in disputes.Select(d => d.ExamId).Distinct())
        {
            names[examId] = (await catalog.FindAsync(examId, cancellationToken))?.Name;
            emails[examId] = (await roster.GetEnrolledCandidatesAsync(examId, cancellationToken)).ToDictionary(c => c.UserId, c => c.Email);
            foreach (var attempt in await attempts.ListForExamAsync(examId, cancellationToken))
                numbers[attempt.Id] = attempt.Number;
        }

        var questions = (await questionBank.GetAsync(disputes.Select(d => d.QuestionId).Distinct().ToList(), cancellationToken))
            .ToDictionary(q => q.Id, q => q.Text);

        return disputes
            .Select(d => new DisputeDto(
                d.Id,
                d.ExamId,
                names[d.ExamId],
                d.AttemptId,
                numbers.TryGetValue(d.AttemptId, out var number) ? number : null,
                d.CandidateId,
                emails[d.ExamId].GetValueOrDefault(d.CandidateId),
                d.QuestionId,
                questions.GetValueOrDefault(d.QuestionId),
                d.Reason,
                d.RaisedAtUtc,
                d.Status,
                d.ResolvedAtUtc,
                d.ResolutionNote))
            .ToList();
    }

    /// <summary>The candidate's own view of a dispute.</summary>
    /// <param name="dispute">The dispute.</param>
    public static MyDisputeDto ForCandidate(Dispute dispute) =>
        new(dispute.Id, dispute.QuestionId, dispute.Reason, dispute.RaisedAtUtc, dispute.Status, dispute.ResolvedAtUtc, dispute.ResolutionNote);
}
