using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>
/// Lets an administrator read the codes candidates are waiting for, so support can help someone
/// whose email or SMS never arrived. Reading a code is as powerful as being the candidate, so
/// every call is written to the audit trail, and only unspent codes of the purposes that
/// <see cref="Domain.OtpPurposeExtensions.IsRevealableToStaff"/> allows are ever returned.
/// </summary>
public sealed class ListOutstandingOtpsHandler(
    IOtpChallengeRepository challengeRepository,
    IAuditLogger auditLogger,
    Clock clock)
{
    /// <summary>The most codes one call returns; an admin narrows with a search, not by paging.</summary>
    public const int MaxResults = 50;

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
        // and how much they saw, and must not become a second place the codes can be read.
        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: query.ActorUserId,
                ActorRole: query.ActorRole,
                Action: "Identity.OtpCodesViewed",
                EntityType: "OtpChallenge",
                EntityId: "list",
                Metadata: new Dictionary<string, string>
                {
                    ["count"] = challenges.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["filtered"] = (!string.IsNullOrWhiteSpace(query.DestinationContains)).ToString(),
                },
                CorrelationId: null),
            cancellationToken);

        return challenges
            .Select(c => new OutstandingOtpDto(
                c.Id, c.Purpose.ToString(), c.Channel.ToString(), c.Destination, c.RevealableCode!, c.ExpiresAtUtc))
            .ToList();
    }
}
