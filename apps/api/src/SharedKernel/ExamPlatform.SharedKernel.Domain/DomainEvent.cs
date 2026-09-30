namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// Base record for domain events raised by aggregates. Each event captures
/// what happened and when, and is dispatched after the aggregate is persisted.
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <summary>UTC instant the event occurred.</summary>
    public DateTime OccurredAtUtc { get; init; } = DateTime.UtcNow;
}
