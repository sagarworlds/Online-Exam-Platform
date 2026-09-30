using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Events;

public sealed record BatchActivatedEvent(Guid BatchId, Guid ExamId) : DomainEvent;
