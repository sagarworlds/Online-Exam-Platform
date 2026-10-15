using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>
/// Lets an administrator read the codes candidates are waiting for, so support can help someone
/// whose email or SMS never arrived. Reading a code is as powerful as being the candidate, so
/// every call is written to the audit trail with the challenge and purpose of each code it showed,
/// and only unspent codes of the purposes that
/// <see cref="Domain.OtpPurposeExtensions.IsRevealableToStaff"/> allows are ever returned.
/// </summary>
public sealed class ListOutstandingOtpsHandler(
    IOtpChallengeRepository challengeRepository,
    IAuditLogger auditLogger,
    Clock clock)
{
    /// <summary>The most codes one call returns; an admin narrows with a search, not by paging.</summary>
    public const int MaxResults = 50;

    /// <summary>The audit metadata key that lists the codes shown, as <c>challengeId:Purpose</c> pairs.</summary>
    public const string ChallengesMetadataKey = "challenges";

    /// <summary>Loads the usable codes and records that they were read.</summary>
    /// <param name="query">The optional destination filter and who is asking.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Up to <see cref="MaxResults"/> codes, those expiring last first.</returns>
    public async Task<IReadOnlyList<OutstandingOtpDto>> HandleAsync(
        ListOutstandingOtpsQuery query, CancellationToken cancellationToken)
    {
        var challenges = await challengeRepository.ListRevealableAsync(
            query.DestinationContains, clock.UtcNow, MaxResults, cancellationToken);

        // Recorded after the read and without the codes themselves: the trail shows who looked
        // and which codes they saw, and must not become a second place the codes can be read.
        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: query.ActorUserId,
                ActorRole: query.ActorRole,
                Action: "Identity.OtpCodesViewed",
                EntityType: "OtpChallenge",
                EntityId: "list",
                Metadata: AuditMetadata(challenges, query.DestinationContains),
                CorrelationId: null),
            cancellationToken);

        return challenges
            .Select(c => new OutstandingOtpDto(
                c.Id, c.Purpose.ToString(), c.Channel.ToString(), c.Destination, c.RevealableCode!, c.ExpiresAtUtc))
            .ToList();
    }

    /// <summary>
    /// What the audit entry says about a read: how many codes, whether the list was narrowed, and which
    /// codes. A code is named by its challenge id and purpose, never by its value or its destination.
    /// The id lets a reviewer match the entry to other records of that challenge; the challenge row
    /// itself is deleted a day after it expires, so the id may stop resolving after that.
    /// </summary>
    private static Dictionary<string, string> AuditMetadata(
        IReadOnlyList<Domain.OtpChallenge> challenges, string? destinationFilter) =>
        new()
        {
            ["count"] = challenges.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["filtered"] = (!string.IsNullOrWhiteSpace(destinationFilter)).ToString(),
            [ChallengesMetadataKey] = string.Join(',', challenges.Select(c => $"{c.Id}:{c.Purpose}")),
        };
}
