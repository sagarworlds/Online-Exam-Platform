using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Events;

/// <summary>A member was given a seat in a batch. Carries the member's id only, never their address.</summary>
public sealed record BatchMemberAddedEvent(Guid BatchId, Guid MemberId) : DomainEvent;
