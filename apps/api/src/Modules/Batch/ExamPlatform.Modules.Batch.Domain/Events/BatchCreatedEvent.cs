using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Events;

public sealed record BatchCreatedEvent(Guid BatchId, Guid ExamId, string Name, Guid CreatedBy) : DomainEvent;
