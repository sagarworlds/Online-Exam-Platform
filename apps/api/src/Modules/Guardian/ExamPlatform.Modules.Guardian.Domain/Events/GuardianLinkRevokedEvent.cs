using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Events;

public sealed record GuardianLinkRevokedEvent(Guid GuardianLinkId, Guid GuardianId, Guid CandidateId) : DomainEvent;
