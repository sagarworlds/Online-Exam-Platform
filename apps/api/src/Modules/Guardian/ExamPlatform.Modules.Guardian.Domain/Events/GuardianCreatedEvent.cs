using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Guardian.Domain.Events;

public sealed record GuardianCreatedEvent(Guid GuardianId, string Email, string FullName) : DomainEvent;
