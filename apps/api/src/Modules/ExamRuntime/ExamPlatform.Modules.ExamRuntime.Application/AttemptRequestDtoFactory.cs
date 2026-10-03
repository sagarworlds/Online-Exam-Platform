using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.Invite.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>Builds the staff view of requests, looking up each exam's name and each candidate's address once per exam.</summary>
public sealed class AttemptRequestDtoFactory(IExamCatalog catalog, IExamRoster roster)
{
    /// <summary>Maps requests to DTOs, keeping their order.</summary>
    /// <param name="requests">The requests to report.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<AttemptRequestDto>> CreateAsync(IReadOnlyList<AttemptRequest> requests, CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string?>();
        var emails = new Dictionary<Guid, IReadOnlyDictionary<Guid, string>>();

        foreach (var examId in requests.Select(r => r.ExamId).Distinct())
        {
            names[examId] = (await catalog.FindAsync(examId, cancellationToken))?.Name;
            emails[examId] = (await roster.GetEnrolledCandidatesAsync(examId, cancellationToken)).ToDictionary(c => c.UserId, c => c.Email);
        }

        return requests
            .Select(r => new AttemptRequestDto(
                r.Id, r.ExamId, names[r.ExamId], r.CandidateId, emails[r.ExamId].GetValueOrDefault(r.CandidateId),
                r.Message, r.RequestedAtUtc, r.Status, r.DecidedAtUtc, r.DecisionNote))
            .ToList();
    }

    /// <summary>The candidate's own view of a request.</summary>
    /// <param name="request">The request.</param>
    public static MyAttemptRequestDto ForCandidate(AttemptRequest request) =>
        new(request.Id, request.Status, request.Message, request.RequestedAtUtc, request.DecidedAtUtc, request.DecisionNote);
}
