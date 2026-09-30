using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Domain.Events;

public sealed record ExamPublishedEvent(Guid ExamId, DateTime ScheduledStartTime) : DomainEvent;
