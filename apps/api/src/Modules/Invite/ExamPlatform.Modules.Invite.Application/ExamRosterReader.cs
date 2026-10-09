using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Contracts;

namespace ExamPlatform.Modules.Invite.Application;

/// <summary>The <see cref="IExamRoster"/> other modules read an exam's enrolled candidates through.</summary>
public sealed class ExamRosterReader(IInviteRepository repository) : IExamRoster
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EnrolledCandidate>> GetEnrolledCandidatesAsync(Guid examId, CancellationToken cancellationToken)
    {
        var accepted = await repository.ListAcceptedForExamAsync(examId, cancellationToken);

        // One row per account: a candidate invited twice (a second code, or a re-invite) is still one candidate. Whoever
        // accepted is recorded on the invite, so an invite without an accepter cannot appear here.
        return accepted
            .Where(invite => invite.AcceptedByUserId is not null)
            .GroupBy(invite => invite.AcceptedByUserId!.Value)
            .Select(group => new EnrolledCandidate(group.Key, group.OrderBy(i => i.AcceptedAt).First().Email))
            .OrderBy(candidate => candidate.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
