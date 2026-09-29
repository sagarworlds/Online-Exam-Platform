using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain.Events;

/// <summary>Raised when a <see cref="ConsentRecord"/> is granted.</summary>
/// <param name="ConsentRecordId">The new consent record's id.</param>
/// <param name="SubjectId">Whose data the consent covers.</param>
/// <param name="Purpose">What the consent covers.</param>
/// <param name="OccurredAtUtc">When the consent was granted.</param>
public sealed record ConsentGrantedEvent(
    Guid ConsentRecordId, Guid SubjectId, ConsentPurpose Purpose, DateTime OccurredAtUtc) : IDomainEvent;
