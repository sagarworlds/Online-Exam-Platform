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
            if (IsMinorOn(DateOfBirthOf(datesOfBirth, attempt.CandidateId), attempt.StartedAtUtc))
            {
                minors.Add(attempt.AttemptId);
            }
        }

        return minors;
    }

    /// <inheritdoc />
    public async Task<bool> IsMinorOnAsync(Guid candidateId, DateTime onUtc, CancellationToken cancellationToken)
    {
        var datesOfBirth = await users.ListDatesOfBirthAsync([candidateId], cancellationToken);
        return IsMinorOn(DateOfBirthOf(datesOfBirth, candidateId), onUtc);
    }

    // Null when no date of birth is on record. Not the dictionary's default value: that would be 0001-01-01, which reads as an adult.
    private static DateOnly? DateOfBirthOf(IReadOnlyDictionary<Guid, DateOnly> datesOfBirth, Guid candidateId) =>
        datesOfBirth.TryGetValue(candidateId, out var dateOfBirth) ? dateOfBirth : null;

    // The one place the minors rule is judged. A candidate with no date of birth on record is a minor, because the platform cannot show
    // they are an adult, so the rule fails safe; see the contract's remarks.
    private static bool IsMinorOn(DateOnly? dateOfBirth, DateTime onUtc) =>
        dateOfBirth is not { } known || User.BandOn(known, onUtc) == AgeBand.Minor;
}
