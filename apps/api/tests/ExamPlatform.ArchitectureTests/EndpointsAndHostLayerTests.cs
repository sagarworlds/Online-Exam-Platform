namespace ExamPlatform.ArchitectureTests;

/// <summary>
/// Enforces ADR 0001's rules for the outermost layers: a module's
/// <c>Endpoints</c> project never depends on another module except through its
/// <c>Contracts</c> (each module maps its own routes independently), and the Host
/// never references a module's Domain, Application, or Infrastructure directly —
/// only its <c>Endpoints</c> project, so it cannot bypass the boundary.
/// </summary>
public class EndpointsAndHostLayerTests
{
    /// <summary>xUnit theory data: every discovered module name.</summary>
    public static IEnumerable<object[]> Modules() => ModuleCatalog.ModuleNames();

    [Theory]
    [MemberData(nameof(Modules))]
    public void Endpoints_ShouldNotDependOnOtherModulesExceptContracts(string moduleName)
    {
        var module = ModuleCatalog.Get(moduleName);

        var disallowed = ModuleCatalog.Others(moduleName)
            .SelectMany(other => ModuleCatalog.KnownLayers
                .Where(layer => layer != "Contracts")
                .Select(other.NamespaceOf));

        DependencyRules.AssertNoDependency(module.Layer("Endpoints"), $"{moduleName}.Endpoints", disallowed);
    }

    [Fact]
    public void Host_ShouldOnlyReferenceModuleEndpoints()
    {
        var host = ModuleCatalog.HostAssembly;

        // Type-level: no code in the Host touches a module's internals ...
        var disallowed = ModuleCatalog.Modules
            .SelectMany(m => ModuleCatalog.KnownLayers
                .Where(layer => layer != "Endpoints")
                .Select(m.NamespaceOf));

        DependencyRules.AssertNoDependency(host, "The Host", disallowed);

        // ... and assembly-level: the only module assemblies it references are Endpoints projects.
        var referencedModuleAssemblies = host.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("ExamPlatform.Modules.", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(referencedModuleAssemblies);
        Assert.All(referencedModuleAssemblies, name => Assert.EndsWith(".Endpoints", name));
    }
}
