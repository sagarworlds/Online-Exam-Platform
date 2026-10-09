using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.SharedKernel.UnitTests;

public class DomainEventHandlerRegistrationTests
{
    public sealed record FirstThingHappened : DomainEvent;

    public sealed record SecondThingHappened : DomainEvent;

    /// <summary>A handler for two events, like a module's audit trail.</summary>
    public sealed class BothHandler : IDomainEventHandler<FirstThingHappened>, IDomainEventHandler<SecondThingHappened>
    {
        public Task HandleAsync(FirstThingHappened domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleAsync(SecondThingHappened domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void AddDomainEventHandlers_RegistersAHandlerOncePerEventItHandles()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandlers(typeof(DomainEventHandlerRegistrationTests).Assembly);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<BothHandler>(Assert.Single(provider.GetServices<IDomainEventHandler<FirstThingHappened>>()));
        Assert.IsType<BothHandler>(Assert.Single(provider.GetServices<IDomainEventHandler<SecondThingHappened>>()));
    }

    [Fact]
    public void AddDomainEventHandlers_LeavesOtherTypesAlone()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandlers(typeof(DomainEventHandlerRegistrationTests).Assembly);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(DomainEventHandlerRegistrationTests));
    }
}
