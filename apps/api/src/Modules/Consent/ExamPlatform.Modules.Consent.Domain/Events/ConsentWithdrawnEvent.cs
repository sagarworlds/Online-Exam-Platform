using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Consent.Domain.Events;

/// <summary>Raised when a <see cref="ConsentRecord"/> is withdrawn.</summary>
/// <param name="ConsentRecordId">The withdrawn consent record's id.</param>
/// <param name="SubjectId">Whose data the consent covered.</param>
/// <param name="Purpose">What the consent covered.</param>
/// <param name="WithdrawnById">Who withdrew the consent (the subject or their guardian).</param>
/// <param name="OccurredAtUtc">When the withdrawal happened.</param>
public sealed record ConsentWithdrawnEvent(
    Guid ConsentRecordId,
    Guid SubjectId,
    ConsentPurpose Purpose,
    Guid WithdrawnById,
    DateTime OccurredAtUtc) : IDomainEvent;
