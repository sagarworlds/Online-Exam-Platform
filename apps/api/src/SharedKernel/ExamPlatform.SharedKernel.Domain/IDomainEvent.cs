namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// Marker interface for something that happened inside an aggregate that other
/// parts of the system may care about. Raised via <see cref="AggregateRoot.AddDomainEvent"/>
/// and dispatched after a successful persistence commit.
/// </summary>
public interface IDomainEvent
{
    /// <summary>UTC instant the event occurred, as recorded by the aggregate.</summary>
    DateTime OccurredAtUtc { get; }
}
