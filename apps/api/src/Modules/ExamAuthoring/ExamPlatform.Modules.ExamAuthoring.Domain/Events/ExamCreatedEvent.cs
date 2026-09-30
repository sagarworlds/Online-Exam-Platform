using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Events;

public sealed record ExamCreatedEvent(Guid ExamId, string Name, Guid CreatedBy) : DomainEvent;
