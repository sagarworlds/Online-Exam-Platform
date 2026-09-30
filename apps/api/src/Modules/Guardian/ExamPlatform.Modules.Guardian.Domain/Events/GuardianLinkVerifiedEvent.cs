using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Events;

public sealed record GuardianLinkVerifiedEvent(Guid GuardianLinkId, Guid GuardianId, Guid CandidateId) : DomainEvent;
