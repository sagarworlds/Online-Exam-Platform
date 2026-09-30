using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Events;

public sealed record BatchClosedEvent(Guid BatchId, Guid ExamId) : DomainEvent;
