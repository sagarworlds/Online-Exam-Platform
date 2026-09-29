using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// Dispatches domain events recorded by aggregates to their registered handlers,
/// after the aggregate's changes have been durably persisted.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches each event to every handler registered for its concrete type.</summary>
    /// <param name="domainEvents">The events to dispatch, typically drained from one or more aggregates.</param>
    /// <param name="cancellationToken">Cancellation token for the dispatch operation.</param>
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

/// <summary>
/// Reacts to a specific kind of domain event. Modules register implementations
/// via dependency injection; new reactions plug in without changing the
/// aggregate that raised the event (Open/Closed Principle).
/// </summary>
/// <typeparam name="TEvent">The concrete domain event type this handler reacts to.</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>Handles one occurrence of the event.</summary>
    /// <param name="domainEvent">The event instance.</param>
    /// <param name="cancellationToken">Cancellation token for the handler.</param>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
