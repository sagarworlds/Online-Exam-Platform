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

    /// <summary>
    /// Tells the candidate how their request was answered, after the answer is saved. Nothing here can undo that answer: a missing
    /// address or a mail failure comes back as <see langword="false"/> for the administrator to act on.
    /// </summary>
    /// <param name="decided">The request as just decided.</param>
    /// <param name="notifier">What sends the e-mail.</param>
    /// <param name="approved">Whether it was approved (otherwise declined).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether an e-mail was handed to a mail server.</returns>
    public async Task<bool> NotifyAsync(AttemptRequestDto decided, IAttemptRequestNotifier notifier, bool approved, CancellationToken cancellationToken)
    {
        // No address means the candidate is no longer enrolled: there is nobody to write to.
        if (decided.CandidateEmail is not { } address)
            return false;

        return await notifier.SendDecisionAsync(
            new AttemptRequestDecisionEmail(address, decided.ExamName ?? "the exam", approved, decided.DecisionNote), cancellationToken);
    }

    /// <summary>The candidate's own view of a request.</summary>
    /// <param name="request">The request.</param>
    public static MyAttemptRequestDto ForCandidate(AttemptRequest request) =>
        new(request.Id, request.Status, request.Message, request.RequestedAtUtc, request.DecidedAtUtc, request.DecisionNote);
}
