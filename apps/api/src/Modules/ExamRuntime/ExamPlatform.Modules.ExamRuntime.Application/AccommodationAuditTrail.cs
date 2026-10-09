using System.Globalization;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Records accommodations being set, and attempts taking them on, in the audit trail (FR-49, FR-40). It reacts to events, as the attempt
/// audit does, and writes what was given and never the staff note, which is sensitive personal information. (Removal is recorded by its
/// handler, since a deleted row is not tracked when events are dispatched.)
/// </summary>
public sealed class AccommodationAuditTrail(IAuditLogger auditLogger, IRequestContext requestContext)
    : IDomainEventHandler<AccommodationSetEvent>,
        IDomainEventHandler<AttemptAccommodatedEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(AccommodationSetEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            "ExamRuntime.AccommodationSet",
            "Accommodation",
            domainEvent.AccommodationId,
            domainEvent.ExamId,
            domainEvent.CandidateId,
            new Dictionary<string, string>
            {
                ["extraTimeSeconds"] = domainEvent.ExtraTimeSeconds.ToString(CultureInfo.InvariantCulture),
                ["readerScribe"] = domainEvent.ReaderScribe ? "true" : "false",
                ["alternateFormats"] = string.Join(",", domainEvent.AlternateFormats),
            },
            cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(AttemptAccommodatedEvent domainEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            "ExamRuntime.AttemptAccommodated",
            "Attempt",
            domainEvent.AttemptId,
            domainEvent.ExamId,
            domainEvent.CandidateId,
            new Dictionary<string, string>
            {
                ["extraTimeSeconds"] = domainEvent.ExtraTimeSeconds.ToString(CultureInfo.InvariantCulture),
                ["addedSeconds"] = domainEvent.AddedSeconds.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);

    private Task RecordAsync(
        string action, string entityType, Guid entityId, Guid examId, Guid candidateId, Dictionary<string, string> extra, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["examId"] = examId.ToString(),
            ["candidateId"] = candidateId.ToString(),
        };
        foreach (var (key, value) in extra)
            metadata[key] = value;

        return auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: requestContext.UserId,
                ActorRole: requestContext.Role,
                Action: action,
                EntityType: entityType,
                EntityId: entityId.ToString(),
                Metadata: metadata,
                CorrelationId: requestContext.CorrelationId),
            cancellationToken);
    }
}
