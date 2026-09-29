using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamPlatform.SharedKernel.Infrastructure;

/// <summary>
/// Dispatches domain events recorded on tracked <see cref="AggregateRoot"/> entities
/// only after <c>SaveChanges</c> has committed successfully — so a handler never
/// reacts to a change that a later failure rolled back — then clears them so a
/// retried save cannot redispatch the same event.
/// </summary>
public sealed class DomainEventsSaveChangesInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is not null)
        {
            var aggregatesWithEvents = context.ChangeTracker
                .Entries<AggregateRoot>()
                .Select(entry => entry.Entity)
                .Where(aggregate => aggregate.DomainEvents.Count > 0)
                .ToList();

            var domainEvents = aggregatesWithEvents.SelectMany(a => a.DomainEvents).ToList();
            foreach (var aggregate in aggregatesWithEvents)
            {
                aggregate.ClearDomainEvents();
            }

            if (domainEvents.Count > 0)
            {
                await dispatcher.DispatchAsync(domainEvents, cancellationToken);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
