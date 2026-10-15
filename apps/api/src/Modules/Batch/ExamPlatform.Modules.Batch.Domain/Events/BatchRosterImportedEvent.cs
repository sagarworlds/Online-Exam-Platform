using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Events;

/// <summary>
/// A roster file gave members seats in a batch. One event for the whole file, not one per member, so a large file
/// costs one audit entry. Carries the members' ids only, never their addresses.
/// </summary>
/// <param name="BatchId">The batch the members were added to.</param>
/// <param name="MemberIds">The members who were given a seat, in file order.</param>
public sealed record BatchRosterImportedEvent(Guid BatchId, IReadOnlyList<Guid> MemberIds) : DomainEvent;
