using System.Reflection;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Every domain event is handled by something, or is on a short list of events that are deliberately not, each with its reason. An
/// event nobody handles does nothing: if the handler for an audit trail (FR-40) was never written, nothing fails and nothing is recorded,
/// so the gap would only be found by someone reading the log for a record that is not there.
/// </summary>
public class DomainEventHandlerTests
{
    // Events raised by an aggregate that nothing handles, by full type name, each with why. An event leaves this list when a handler is
    // written for it (the test below fails until it is removed), and a new event needs a handler or an entry here with its reason.
    private static readonly IReadOnlyDictionary<string, string> NotHandled = new Dictionary<string, string>
    {
        ["ExamPlatform.Modules.Identity.Domain.Events.UserRegisteredEvent"] =
            "Raised when an account is created. IdentityAuditTrail handles only SessionSupersededEvent, so a registration leaves no audit " +
            "record. Whether it should (FR-40, the audit log of admin and content changes) is the owner's decision, not made here.",
        ["ExamPlatform.Modules.Guardian.Domain.Events.GuardianCreatedEvent"] = GuardianReason,
        ["ExamPlatform.Modules.Guardian.Domain.Events.GuardianLinkVerifiedEvent"] = GuardianReason,
        ["ExamPlatform.Modules.Guardian.Domain.Events.GuardianLinkRevokedEvent"] = GuardianReason,
        ["ExamPlatform.Modules.Consent.Domain.Events.ConsentGrantedEvent"] =
            "A grant is kept as a ConsentRecord and writes no audit entry; only a withdrawal does. Whether a grant belongs in the audit log " +
            "(FR-40) is the owner's decision, not made here.",
        ["ExamPlatform.Modules.Consent.Domain.Events.ConsentWithdrawnEvent"] =
            "ConsentService.WithdrawConsentAsync writes the 'Consent.Withdrawn' audit entry itself, after saving, so nothing needs to " +
            "handle the event today.",
    };

    private const string GuardianReason =
        "The Guardian module has no audit trail, so registering a guardian and confirming or revoking a link leave no audit record. " +
        "Whether it should (FR-40) is the owner's decision, not made here.";

    private static readonly Lazy<IReadOnlyList<Assembly>> ModuleAssemblies = new(() =>
        ModuleCatalog.Modules.SelectMany(module => module.Layers.Values).ToList());

    /// <summary>The domain events of every module, and of the shared kernel: concrete types that implement <see cref="IDomainEvent"/>.</summary>
    private static IReadOnlyList<Type> Events() =>
        ModuleAssemblies.Value.Append(typeof(IDomainEvent).Assembly)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>The event types some class handles, by the exact type, which is how the dispatcher looks handlers up.</summary>
    private static IReadOnlySet<Type> HandledEvents() =>
        ModuleAssemblies.Value.Append(ModuleCatalog.HostAssembly)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

    /// <summary>The events nothing handles, ordered by name.</summary>
    private static IReadOnlyList<Type> EventsWithoutAHandler(IEnumerable<Type> events, IReadOnlySet<Type> handled) =>
        events.Where(type => !handled.Contains(type)).OrderBy(type => type.FullName, StringComparer.Ordinal).ToList();

    [Fact]
    public void EveryDomainEvent_HasAHandler_OrIsOnTheListOfEventsLeftUnhandledWithAReason()
    {
        var unexplained = EventsWithoutAHandler(Events(), HandledEvents()).Where(type => !NotHandled.ContainsKey(type.FullName!)).ToList();

        Assert.True(
            unexplained.Count == 0,
            "These domain events are raised but nothing handles them: " + string.Join(", ", unexplained.Select(type => type.FullName)) +
            ". Write an IDomainEventHandler for each (an audit trail, if FR-40 applies), or add it to NotHandled with the reason.");
    }

    [Fact]
    public void TheListOfUnhandledEvents_NamesOnlyEventsThatStillExistAndStillHaveNoHandler()
    {
        var events = Events();
        var withoutAHandler = EventsWithoutAHandler(events, HandledEvents()).Select(type => type.FullName!).ToHashSet();

        var gone = NotHandled.Keys.Where(name => events.All(type => type.FullName != name)).ToList();
        var handledNow = NotHandled.Keys.Where(name => !withoutAHandler.Contains(name) && !gone.Contains(name)).ToList();

        Assert.True(gone.Count == 0, "NotHandled lists events that no longer exist; remove them: " + string.Join(", ", gone));
        Assert.True(handledNow.Count == 0, "NotHandled lists events that now have a handler; remove them: " + string.Join(", ", handledNow));
        Assert.All(NotHandled.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    // ---- controls: the rule can see events and handlers, and can fail ---------------------------------------------------------

    private sealed record HandledThing : DomainEvent;

    private sealed record UnhandledThing : DomainEvent;

    private sealed class ThingHandler : IDomainEventHandler<HandledThing>
    {
        public Task HandleAsync(HandledThing domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Control_AnEventWithNoHandlerIsFound_AndAHandledOneIsNot()
    {
        var handled = typeof(ThingHandler).GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var found = EventsWithoutAHandler(new[] { typeof(HandledThing), typeof(UnhandledThing) }, handled);

        Assert.Equal(typeof(UnhandledThing), Assert.Single(found));
    }

    [Fact]
    public void Control_TheDiscoveryFindsTheRealEventsAndHandlers()
    {
        var events = Events().Select(type => type.FullName).ToList();
        var handled = HandledEvents().Select(type => type.FullName).ToList();

        // An exam's creation is audited by ExamAuditTrail, and a registration is not, so the rule sees both kinds in the real assemblies.
        Assert.Contains("ExamPlatform.Modules.ExamAuthoring.Domain.Events.ExamCreatedEvent", handled);
        Assert.Contains("ExamPlatform.Modules.Identity.Domain.Events.UserRegisteredEvent", events);
        Assert.DoesNotContain("ExamPlatform.Modules.Identity.Domain.Events.UserRegisteredEvent", handled);
        Assert.True(events.Count >= 30, $"Expected the platform's domain events to be found, but found {events.Count}.");
    }
}
