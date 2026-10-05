using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Events;

/// <summary>A member was taken out of a batch. Carries the member's id only, never their address.</summary>
public sealed record BatchMemberRemovedEvent(Guid BatchId, Guid MemberId) : DomainEvent;
