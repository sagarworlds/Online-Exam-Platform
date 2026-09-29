namespace ExamPlatform.SharedKernel.Domain;

/// <summary>
/// Base type for aggregate roots: entities that are the sole entry point for
/// modifications to the object graph they own, and that record domain events
/// for anything other modules or handlers may need to react to.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Initializes a new aggregate root with the given identifier.</summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    protected AggregateRoot(Guid id) : base(id)
    {
    }

    /// <summary>Domain events raised by this aggregate since it was loaded or created.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Records a domain event to be dispatched after this aggregate is successfully persisted.</summary>
    /// <param name="domainEvent">The event that occurred.</param>
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears recorded domain events. Called by infrastructure once the events
    /// have been dispatched, so a retried save does not redispatch them.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
