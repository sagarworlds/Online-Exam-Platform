using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>The <see cref="ICandidateAgeDirectory"/> other modules ask whether an attempt was sat by a minor.</summary>
public sealed class CandidateAgeDirectory(IUserRepository users) : ICandidateAgeDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> FindAttemptsSatAsMinorAsync(IReadOnlyCollection<AttemptStart> attempts, CancellationToken cancellationToken)
    {
        if (attempts.Count == 0)
        {
            return new HashSet<Guid>();
        }

        // One read for every candidate, however many attempts they sat: the dates are looked up once, then each attempt is judged by its
        // own start time.
        var candidateIds = attempts.Select(a => a.CandidateId).Distinct().ToList();
        var datesOfBirth = await users.ListDatesOfBirthAsync(candidateIds, cancellationToken);

        var minors = new HashSet<Guid>();
        foreach (var attempt in attempts)
        {
            // A candidate with no date of birth on record is a minor for this purpose: see the contract's remarks.
            if (!datesOfBirth.TryGetValue(attempt.CandidateId, out var dateOfBirth)
                || User.BandOn(dateOfBirth, attempt.StartedAtUtc) == AgeBand.Minor)
            {
                minors.Add(attempt.AttemptId);
            }
        }

        return minors;
    }
}
