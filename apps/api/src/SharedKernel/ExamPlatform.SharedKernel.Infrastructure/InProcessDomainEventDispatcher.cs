using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.Infrastructure;

/// <summary>
/// Dispatches domain events to handlers resolved from the DI container.
/// In-process only: a handler runs inline, in the same request/transaction scope
/// as the save that raised the event. A message-queue-backed dispatcher can
/// implement <see cref="IDomainEventDispatcher"/> later without any change to
/// the aggregates that raise events (Open/Closed Principle) — deferred until a
/// module actually needs durable, cross-process delivery (see ADR 0001).
/// </summary>
public sealed class InProcessDomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<InProcessDomainEventDispatcher> logger) : IDomainEventDispatcher
{
    /// <inheritdoc />
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handlers = serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                // A handler failing to run should not be swallowed: an admin action or
                // audit trail silently not happening is a correctness bug, not a warning.
                var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))
                    ?? throw new InvalidOperationException($"Handler type {handlerType} is missing HandleAsync.");

                try
                {
                    await (Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(
                        ex,
                        "Domain event handler {HandlerType} failed while handling {EventType}",
                        handler!.GetType().Name,
                        domainEvent.GetType().Name);
                    throw;
                }
            }
        }
    }
}
